namespace SyncMaid.Core.Triggers;

/// <summary>
/// Serialized, order-preserving delivery of trigger notifications: decide under the
/// owner's state gate, deliver outside it, in decided order. Owners enqueue
/// notifications (and bump the epoch on Stop/Dispose) while holding their state gate,
/// then call <see cref="Drain"/> after releasing it. Guarantees:
/// <list type="bullet">
/// <item>subscribers never run under the owner's state gate, so a slow handler cannot
/// block watcher/timer callbacks or state transitions;</item>
/// <item>deliveries are serialized and land in enqueue order, so an Error and a
/// Recovered decided in sequence can never be observed crossed;</item>
/// <item><see cref="Invalidate"/> + <see cref="WaitForIdle"/> preserve the Stop
/// contract — entries decided before Stop never deliver after it, and a delivery
/// already in flight completes before Stop returns ("the fire has quiesced").</item>
/// </list>
/// Both internal locks are reentrant, so a subscriber may call the owner's
/// Stop/Dispose from inside its own delivery without deadlocking.
/// </summary>
internal sealed class TriggerNotifier
{
    private readonly Lock _queueGate = new();
    private readonly Lock _deliveryGate = new();
    private readonly Queue<(long Epoch, Action Delivery)> _pending = new();
    private long _epoch;

    /// <summary>Queues a delivery under the current epoch. Call while holding the
    /// owner's state gate so queue order matches decision order.</summary>
    public void Enqueue(Action delivery)
    {
        lock (_queueGate)
        {
            _pending.Enqueue((_epoch, delivery));
        }
    }

    /// <summary>Drops every queued-but-undelivered entry. Call from Stop/Dispose while
    /// holding the owner's state gate, so notifications decided before the transition
    /// can never deliver after it.</summary>
    public void Invalidate()
    {
        lock (_queueGate)
        {
            _epoch++;
        }
    }

    /// <summary>
    /// Delivers queued entries in order until the queue is empty. Non-blocking when a
    /// drain is already active on another thread: that drainer re-checks the queue after
    /// releasing the gate, so entries enqueued here are never stranded — and never
    /// delivered concurrently or out of order. Call only after releasing the owner's
    /// state gate. <paramref name="onDeliveryError"/> observes a throwing subscriber; it
    /// may decide and enqueue follow-up notifications, which the same drain delivers.
    /// </summary>
    public void Drain(Action<Exception>? onDeliveryError = null)
    {
        while (true)
        {
            if (!_deliveryGate.TryEnter())
            {
                return; // the active drainer delivers our entries via its re-check below
            }

            try
            {
                DeliverAll(onDeliveryError);
            }
            finally
            {
                _deliveryGate.Exit();
            }

            lock (_queueGate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
            }

            // Entries raced in between the empty check and the gate release; claim again.
        }
    }

    private void DeliverAll(Action<Exception>? onDeliveryError)
    {
        while (true)
        {
            Action? delivery = null;
            lock (_queueGate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                var (epoch, queued) = _pending.Dequeue();
                if (epoch == _epoch)
                {
                    delivery = queued;
                }
            }

            if (delivery is null)
            {
                continue; // stale entry from before an Invalidate
            }

            try
            {
                delivery();
            }
            catch (Exception exception)
            {
                try
                {
                    onDeliveryError?.Invoke(exception);
                }
                catch
                {
                    // The drain runs on thread-pool callbacks; nothing may escape it.
                }
            }
        }
    }

    /// <summary>Blocks until any in-flight delivery completes — the Stop/Dispose
    /// barrier. Call after releasing the owner's state gate (a delivering subscriber
    /// may need that gate to finish). Reentrant: a subscriber calling Stop from inside
    /// its own delivery passes straight through.</summary>
    public void WaitForIdle()
    {
        lock (_deliveryGate)
        {
        }
    }
}
