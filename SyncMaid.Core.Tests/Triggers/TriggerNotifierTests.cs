using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class TriggerNotifierTests
{
    [Fact]
    public void Deliveries_run_in_enqueue_order()
    {
        var notifier = new TriggerNotifier();
        var delivered = new List<string>();
        notifier.Enqueue(() => delivered.Add("error"));
        notifier.Enqueue(() => delivered.Add("recovered"));
        notifier.Enqueue(() => delivered.Add("fired"));

        notifier.Drain();

        Assert.Equal(["error", "recovered", "fired"], delivered);
    }

    [Fact]
    public void Invalidated_entries_never_deliver()
    {
        var notifier = new TriggerNotifier();
        var delivered = new List<string>();
        notifier.Enqueue(() => delivered.Add("stale"));
        notifier.Invalidate();
        notifier.Enqueue(() => delivered.Add("current"));

        notifier.Drain();

        Assert.Equal(["current"], delivered);
    }

    // The stuck-badge race: two reporters deciding Error and Recovered concurrently must
    // deliver in decision order, never crossed. A concurrent drain returns without
    // blocking; the active drainer picks up the late entry via its re-check.
    [Fact]
    public async Task Concurrent_enqueues_deliver_in_order_through_the_active_drain()
    {
        var notifier = new TriggerNotifier();
        var delivered = new List<string>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        notifier.Enqueue(() =>
        {
            firstEntered.TrySetResult();
            releaseFirst.Wait();
            delivered.Add("first");
        });

        var drainA = Task.Run(() => notifier.Drain());
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        notifier.Enqueue(() => delivered.Add("second"));
        var drainB = Task.Run(() => notifier.Drain()); // blocks behind drainA
        await Task.Delay(50);

        Assert.Empty(delivered); // nothing lands out of order while the first is in flight
        releaseFirst.Set();
        await Task.WhenAll(drainA, drainB).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["first", "second"], delivered);
    }

    [Fact]
    public async Task Wait_for_idle_blocks_until_the_inflight_delivery_completes()
    {
        var notifier = new TriggerNotifier();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        notifier.Enqueue(() =>
        {
            entered.TrySetResult();
            release.Wait();
        });

        var drain = Task.Run(() => notifier.Drain());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var idle = Task.Run(notifier.WaitForIdle);
        await Task.Delay(50);

        Assert.False(idle.IsCompleted); // the Stop barrier waits out the delivery
        release.Set();
        await Task.WhenAll(drain, idle).WaitAsync(TimeSpan.FromSeconds(1));
    }

    // A subscriber calling the owner's Stop from inside its own delivery reaches
    // Invalidate + WaitForIdle on the drain thread — reentrancy must let it through,
    // and entries queued behind it must drop.
    [Fact]
    public void A_delivery_may_invalidate_and_wait_without_deadlock()
    {
        var notifier = new TriggerNotifier();
        var delivered = new List<string>();
        notifier.Enqueue(() =>
        {
            delivered.Add("first");
            notifier.Invalidate();
            notifier.WaitForIdle();
        });
        notifier.Enqueue(() => delivered.Add("second"));

        notifier.Drain();

        Assert.Equal(["first"], delivered);
    }

    [Fact]
    public void Delivery_failures_route_to_the_callback_and_the_drain_continues()
    {
        var notifier = new TriggerNotifier();
        var delivered = new List<string>();
        Exception? observed = null;
        notifier.Enqueue(() => throw new InvalidOperationException("subscriber failed"));
        notifier.Enqueue(() => delivered.Add("after"));

        notifier.Drain(exception =>
        {
            observed = exception;
            // The owner's callback typically decides and enqueues a follow-up error,
            // which the same drain must then deliver.
            notifier.Enqueue(() => delivered.Add("error"));
        });

        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(["after", "error"], delivered);
    }
}
