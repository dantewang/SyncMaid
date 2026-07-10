namespace SyncMaid.Core.Triggers;

/// <summary>
/// Fires on a cron schedule. Uses a one-shot <see cref="Timer"/> that re-arms itself
/// for the next occurrence after each fire, so drift does not accumulate. The
/// occurrence math lives in <see cref="CronSchedule"/> (pure, unit-tested); this type
/// only owns the timer.
/// </summary>
public sealed class ScheduledTriggerSource : ITriggerSource
{
    internal static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly string _cronExpression;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<Action, IOneShotTimer> _timerFactory;
    private readonly Lock _gate = new();
    private IOneShotTimer? _timer;
    private DateTime? _nextOccurrenceUtc;
    private bool _stopped = true;
    private bool _disposed;

    public ScheduledTriggerSource(string cronExpression)
        : this(cronExpression, () => DateTime.UtcNow, callback => new SystemOneShotTimer(callback))
    {
    }

    internal ScheduledTriggerSource(
        string cronExpression,
        Func<DateTime> utcNow,
        Func<Action, IOneShotTimer> timerFactory)
    {
        _cronExpression = cronExpression;
        _utcNow = utcNow;
        _timerFactory = timerFactory;
    }

    /// <inheritdoc />
    public event EventHandler? Fired;

    /// <inheritdoc />
    public event Action<Exception>? Error;

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
        }
    }

    private void OnTimer()
    {
        var occurrenceReached = false;
        try
        {
            lock (_gate)
            {
                if (_disposed || _stopped)
                {
                    return;
                }

                var now = _utcNow();
                if (_nextOccurrenceUtc is not { } next)
                {
                    ArmNext();
                    return;
                }

                if (now < next)
                {
                    ArmUntil(next, now);
                    return;
                }

                occurrenceReached = true;
                _nextOccurrenceUtc = null;

                // Keep Stop mutually exclusive with delivery: once Stop returns, no callback
                // that already passed the stopped check can still notify. The lock is reentrant,
                // so a handler may safely call Stop/Dispose itself.
                Fired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            if (occurrenceReached)
            {
                try
                {
                    lock (_gate)
                    {
                        if (!_disposed && !_stopped)
                        {
                            ArmNext();
                        }
                    }
                }
                catch (Exception exception)
                {
                    ReportError(exception);
                }
            }
        }
    }

    // Schedules the timer for the next cron occurrence. Caller holds _gate.
    private void ArmNext()
    {
        var now = _utcNow();
        _nextOccurrenceUtc = CronSchedule.NextOccurrenceUtc(_cronExpression, now);
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

    private void ReportError(Exception exception)
    {
        try
        {
            Error?.Invoke(exception);
        }
        catch
        {
            // This is the thread-pool boundary; subscriber failures must not escape it either.
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
        }
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
