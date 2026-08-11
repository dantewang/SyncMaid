using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class ScheduledTriggerSourceTests
{
    // The timer fires because the clock reached the occurrence, not because the test said
    // so — the property every other test in this class takes on faith.
    [Fact]
    public void A_daily_schedule_fires_once_per_day_at_the_scheduled_time()
    {
        var clock = new VirtualClock(new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc));
        var firedAt = new List<DateTime>();
        using var source = new ScheduledTriggerSource(
            "0 2 * * *", () => clock.UtcNow, clock.CreateTimer, TimeZoneInfo.Utc);
        source.Fired += (_, _) => firedAt.Add(clock.UtcNow);

        source.Start();
        clock.AdvanceTo(new DateTime(2026, 3, 4, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(
            [
                new DateTime(2026, 3, 2, 2, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 3, 2, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 4, 2, 0, 0, DateTimeKind.Utc),
            ],
            firedAt);
    }

    // A laptop asleep 22:00-08:00 misses its 02:00 backup every night. The timer does not
    // advance across sleep, so on resume it is simply overdue: the run must happen once as
    // a catch-up and re-arm to the next *future* occurrence — not fire once per night
    // missed, and not re-arm into the past and spin.
    [Fact]
    public void A_schedule_missed_while_the_machine_slept_fires_once_and_rearms_ahead()
    {
        var clock = new VirtualClock(new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc));
        var firedAt = new List<DateTime>();
        using var source = new ScheduledTriggerSource(
            "0 2 * * *", () => clock.UtcNow, clock.CreateTimer, TimeZoneInfo.Utc);
        source.Fired += (_, _) => firedAt.Add(clock.UtcNow);

        source.Start();

        // Three nights pass with the machine asleep; it wakes mid-morning on the fourth.
        clock.SleepThrough(new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc));

        var wake = Assert.Single(firedAt);
        Assert.Equal(new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc), wake);

        // And the schedule is still live: the next occurrence runs on time.
        clock.AdvanceTo(new DateTime(2026, 3, 5, 3, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 3, 5, 2, 0, 0, DateTimeKind.Utc), firedAt.Last());
        Assert.Equal(2, firedAt.Count);
    }

    // The night the clock goes back, 01:30 local happens twice. A backup product must run
    // it once, not twice — a duplicated Mirror run is wasted work, and the symmetric
    // spring-forward case (a time that does not exist) is already pinned in
    // CronScheduleTests. Every other test here uses UTC, which never transitions.
    [Fact]
    public void An_ambiguous_local_time_on_the_dst_fall_back_night_fires_once()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        // 2026-11-01: US clocks go back 02:00 -> 01:00, so 01:30 EDT and 01:30 EST both
        // exist. Start the evening before, in UTC.
        var clock = new VirtualClock(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc));
        var fires = 0;
        using var source = new ScheduledTriggerSource(
            "30 1 * * *", () => clock.UtcNow, clock.CreateTimer, eastern);
        source.Fired += (_, _) => fires++;

        source.Start();
        clock.AdvanceTo(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, fires);
    }

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

    [Fact]
    public void A_successful_tick_after_a_boundary_failure_reports_recovery()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var shouldThrow = true;
        var errors = 0;
        var recoveries = 0;
        source.Fired += (_, _) =>
        {
            if (shouldThrow)
            {
                throw new InvalidOperationException("first tick failed");
            }
        };
        source.Error += _ => errors++;
        source.Recovered += () => recoveries++;
        source.Start();

        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);
        timer!.Fire();
        shouldThrow = false;
        now = new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc);
        timer.Fire();

        Assert.Equal(1, errors);
        Assert.Equal(1, recoveries);
    }

    // The badge must not clear on a source the user just stopped: a fire whose handler
    // stops the source suppresses the recovery that tick would otherwise have reported.
    [Fact]
    public void Recovery_is_not_reported_after_a_handler_stops_the_source()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
        FakeTimer? timer = null;
        using var source = New("* * * * *", () => now, value => timer = value);
        var shouldThrow = true;
        var recoveries = 0;
        source.Fired += (_, _) =>
        {
            if (shouldThrow)
            {
                throw new InvalidOperationException("first tick failed");
            }

            source.Stop();
        };
        source.Recovered += () => recoveries++;
        source.Start();

        now = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);
        timer!.Fire();                                    // error state set
        shouldThrow = false;
        now = new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc);
        timer.Fire();                                     // succeeds, but the handler stopped us

        Assert.Equal(0, recoveries);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.LastDueTime);
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
            },
            TimeZoneInfo.Utc);
    }

    /// <summary>
    /// A clock plus the one-shot timer armed against it. <see cref="FakeTimer"/> only
    /// records the due time and leaves firing to the test, so the suite verifies "given a
    /// callback at moment X the right thing happens" and never "the callback happens at
    /// moment X". Here the timer fires because the clock reached its due time, which is
    /// what makes missed occurrences and DST transitions observable.
    /// </summary>
    private sealed class VirtualClock(DateTime startUtc)
    {
        private VirtualTimer? _timer;

        public DateTime UtcNow { get; private set; } = startUtc;

        public ScheduledTriggerSource.IOneShotTimer CreateTimer(Action callback) =>
            _timer = new VirtualTimer(this, callback);

        /// <summary>Runs time forward, firing the armed timer whenever the clock reaches
        /// its due time — an ordinary machine that stays awake.</summary>
        public void AdvanceTo(DateTime targetUtc)
        {
            // Bounded so a schedule that re-arms at zero delay fails the test instead of
            // spinning forever.
            for (var step = 0; step < 1000; step++)
            {
                if (_timer?.DueAtUtc is not { } dueAt || dueAt > targetUtc)
                {
                    break;
                }

                UtcNow = dueAt;
                _timer.FireDue();
            }

            if (UtcNow < targetUtc)
            {
                UtcNow = targetUtc;
            }
        }

        /// <summary>Jumps time forward <b>without</b> firing anything, then delivers a
        /// single late callback if the timer is overdue — a machine that slept through the
        /// occurrence. A System.Threading.Timer does not advance across S3/S4, so on resume
        /// an overdue timer fires once, not once per occurrence missed.</summary>
        public void SleepThrough(DateTime wakeUtc)
        {
            UtcNow = wakeUtc;
            if (_timer?.DueAtUtc is { } dueAt && dueAt <= wakeUtc)
            {
                _timer.FireDue();
            }
        }

        private sealed class VirtualTimer(VirtualClock clock, Action callback)
            : ScheduledTriggerSource.IOneShotTimer
        {
            public DateTime? DueAtUtc { get; private set; }

            public void Change(TimeSpan dueTime) =>
                DueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : clock.UtcNow + dueTime;

            public void FireDue()
            {
                DueAtUtc = null;
                callback();
            }

            public void Dispose() => DueAtUtc = null;
        }
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
