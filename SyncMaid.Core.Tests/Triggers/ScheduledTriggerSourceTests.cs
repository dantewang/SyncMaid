using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class ScheduledTriggerSourceTests
{
    [Fact]
    public void Long_cron_delay_is_chained_without_firing_early()
    {
        var now = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("0 0 1 1 *", () => now, value => timer = value);
        var fires = 0;
        source.Fired += (_, _) => fires++;

        source.Start();

        Assert.Equal(ScheduledTriggerSource.MaxTimerDueTime, timer!.LastDueTime);
        timer.Fire();
        Assert.Equal(0, fires);
        Assert.Equal(ScheduledTriggerSource.MaxTimerDueTime, timer.LastDueTime);
    }

    [Fact]
    public void Fire_rearms_for_the_next_occurrence()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var fires = 0;
        source.Fired += (_, _) => fires++;
        source.Start();

        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);
        timer!.Fire();

        Assert.Equal(1, fires);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.LastDueTime);
    }

    [Fact]
    public void Stop_from_inside_a_fire_is_not_undone_by_rearm()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var fires = 0;
        source.Fired += (_, _) =>
        {
            fires++;
            source.Stop();
        };
        source.Start();
        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        timer!.Fire();
        timer.Fire();

        Assert.Equal(1, fires);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.LastDueTime);
    }

    [Fact]
    public async Task Stop_racing_a_fire_returns_only_after_the_fire_is_quiesced()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseHandler = new ManualResetEventSlim();
        var fires = 0;
        source.Fired += (_, _) =>
        {
            handlerEntered.TrySetResult();
            releaseHandler.Wait();
            fires++;
        };
        source.Start();
        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        var fire = Task.Run(timer!.Fire);
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var stop = Task.Run(source.Stop);
        await Task.Delay(50);

        Assert.False(stop.IsCompleted);
        releaseHandler.Set();
        await Task.WhenAll(fire, stop).WaitAsync(TimeSpan.FromSeconds(1));
        timer!.Fire();
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Dispose_during_a_fire_is_safe_and_does_not_rearm()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        var source = New("* * * * *", () => now, value => timer = value);
        source.Fired += (_, _) => source.Dispose();
        source.Start();
        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        var exception = Record.Exception(() => timer!.Fire());

        Assert.Null(exception);
        Assert.True(timer!.Disposed);
        Assert.Equal(1, timer.ChangeCount);
    }

    [Fact]
    public void Throwing_fired_handler_is_reported_and_never_escapes_the_timer_callback()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var expected = new InvalidOperationException("handler failed");
        Exception? reported = null;
        source.Fired += (_, _) => throw expected;
        source.Error += exception => reported = exception;
        source.Start();
        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        var escaped = Record.Exception(() => timer!.Fire());

        Assert.Null(escaped);
        Assert.Same(expected, reported);
        Assert.Equal(TimeSpan.FromMinutes(1), timer!.LastDueTime);
    }

    private static ScheduledTriggerSource New(
        string cron,
        Func<DateTime> utcNow,
        Action<FakeTimer> capture)
    {
        return new ScheduledTriggerSource(
            cron,
            utcNow,
            callback =>
            {
                var timer = new FakeTimer(callback);
                capture(timer);
                return timer;
            });
    }

    private sealed class FakeTimer(Action callback) : ScheduledTriggerSource.IOneShotTimer
    {
        public TimeSpan LastDueTime { get; private set; }
        public int ChangeCount { get; private set; }
        public bool Disposed { get; private set; }

        public void Change(TimeSpan dueTime)
        {
            LastDueTime = dueTime;
            ChangeCount++;
        }

        public void Fire() => callback();

        public void Dispose() => Disposed = true;
    }
}
