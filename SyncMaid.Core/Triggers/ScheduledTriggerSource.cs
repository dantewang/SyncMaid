namespace SyncMaid.Core.Triggers;

/// <summary>
/// Fires on a cron schedule interpreted in the machine's local time zone. Uses a
/// one-shot <see cref="Timer"/> that re-arms itself for the next occurrence after each
/// fire, so drift does not accumulate. The
/// occurrence math lives in <see cref="CronSchedule"/> (pure, unit-tested); this type
/// only owns the timer.
/// </summary>
public sealed class ScheduledTriggerSource : ITriggerSource
{
    internal static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly string _cronExpression;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<Action, IOneShotTimer> _timerFactory;
    private readonly TimeZoneInfo _timeZone;
    private readonly Lock _gate = new();
    private readonly TriggerNotifier _notifier = new();
    private IOneShotTimer? _timer;
    private DateTime? _nextOccurrenceUtc;
    private bool _stopped = true;
    private bool _errorReported;
    private bool _disposed;

    public ScheduledTriggerSource(string cronExpression)
        : this(cronExpression, () => DateTime.UtcNow, callback => new SystemOneShotTimer(callback))
    {
    }

    internal ScheduledTriggerSource(
        string cronExpression,
        Func<DateTime> utcNow,
        Func<Action, IOneShotTimer> timerFactory,
        TimeZoneInfo? timeZone = null)
    {
        _cronExpression = cronExpression;
        _utcNow = utcNow;
        _timerFactory = timerFactory;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    /// <inheritdoc />
    public event EventHandler? Fired;

    /// <inheritdoc />
    public event Action<Exception>? Error;

    /// <inheritdoc />
    public event Action? Recovered;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _stopped = false;
            _timer ??= _timerFactory(OnTimer);
            ArmNext();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _nextOccurrenceUtc = null;
            _timer?.Change(Timeout.InfiniteTimeSpan);
            _notifier.Invalidate();
        }

        // Once Stop returns, the fire has quiesced: queued notifications were dropped by
        // the epoch bump, and a delivery already in flight completes inside this barrier.
        _notifier.WaitForIdle();
    }

    private void OnTimer()
    {
        // Decisions happen under the gate and enqueue their notifications; delivery
        // happens outside it via the notifier, which preserves decision order and the
        // Stop contract without subscribers ever running under the state gate.
        var occurrenceReached = false;
        var boundaryFailed = false;
        lock (_gate)
        {
            if (_disposed || _stopped)
            {
                return;
            }

            try
            {
                var now = _utcNow();
                if (_nextOccurrenceUtc is not { } next)
                {
                    ArmNext();
                }
                else if (now < next)
                {
                    ArmUntil(next, now);
                }
                else
                {
                    occurrenceReached = true;
                    _nextOccurrenceUtc = null;
                    _notifier.Enqueue(() => Fired?.Invoke(this, EventArgs.Empty));
                }
            }
            catch (Exception exception)
            {
                boundaryFailed = true;
                EnqueueErrorLocked(exception);
            }
        }

        var deliveryFailed = false;
        _notifier.Drain(exception =>
        {
            deliveryFailed = true;
            OnDeliveryError(exception);
        });

        if (occurrenceReached)
        {
            lock (_gate)
            {
                if (!_disposed && !_stopped)
                {
                    try
                    {
                        ArmNext();
                    }
                    catch (Exception exception)
                    {
                        boundaryFailed = true;
                        EnqueueErrorLocked(exception);
                    }
                }
            }
        }

        if (!boundaryFailed && !deliveryFailed)
        {
            lock (_gate)
            {
                EnqueueRecoveredLocked();
            }
        }

        _notifier.Drain(OnDeliveryError);
    }

    // Schedules the timer for the next cron occurrence. Caller holds _gate.
    private void ArmNext()
    {
        var now = _utcNow();
        _nextOccurrenceUtc = CronSchedule.NextOccurrenceUtc(_cronExpression, now, _timeZone);
        if (_nextOccurrenceUtc is { } next)
        {
            ArmUntil(next, now);
        }
        else
        {
            _timer?.Change(Timeout.InfiniteTimeSpan);
        }
    }

    private void ArmUntil(DateTime next, DateTime now)
    {
        var delay = next - now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (delay > MaxTimerDueTime)
        {
            delay = MaxTimerDueTime;
        }

        _timer?.Change(delay);
    }

    // Error/Recovered transitions are decided atomically with their enqueue, under the
    // gate — the decided order is the delivered order, so the flag and the last
    // delivered event can never disagree. Their subscribers' own failures are swallowed
    // at delivery (they share the thread-pool boundary); a throwing Fired subscriber is
    // surfaced through the drain's error callback instead.
    private void EnqueueErrorLocked(Exception exception)
    {
        _errorReported = true;
        _notifier.Enqueue(() =>
        {
            try
            {
                Error?.Invoke(exception);
            }
            catch
            {
                // Subscriber failures must not escape the thread-pool boundary.
            }
        });
    }

    private void EnqueueRecoveredLocked()
    {
        // Never promise recovery on a source the consumer just stopped or disposed.
        if (_stopped || _disposed || !_errorReported)
        {
            return;
        }

        _errorReported = false;
        _notifier.Enqueue(() =>
        {
            try
            {
                Recovered?.Invoke();
            }
            catch
            {
                // Recovery observers share the same boundary as Error observers.
            }
        });
    }

    private void OnDeliveryError(Exception exception)
    {
        lock (_gate)
        {
            EnqueueErrorLocked(exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _stopped = true;
            _nextOccurrenceUtc = null;
            _timer?.Dispose();
            _timer = null;
            _notifier.Invalidate();
        }

        _notifier.WaitForIdle();
    }

    internal interface IOneShotTimer : IDisposable
    {
        void Change(TimeSpan dueTime);
    }

    private sealed class SystemOneShotTimer : IOneShotTimer
    {
        private readonly Timer _timer;

        public SystemOneShotTimer(Action callback) =>
            _timer = new Timer(_ => callback(), state: null, Timeout.Infinite, Timeout.Infinite);

        public void Change(TimeSpan dueTime) =>
            _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

        public void Dispose() => _timer.Dispose();
    }
}
