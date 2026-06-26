namespace SyncMaid.Core.Triggers;

/// <summary>
/// Fires on a cron schedule. Uses a one-shot <see cref="Timer"/> that re-arms itself
/// for the next occurrence after each fire, so drift does not accumulate. The
/// occurrence math lives in <see cref="CronSchedule"/> (pure, unit-tested); this type
/// only owns the timer.
/// </summary>
public sealed class ScheduledTriggerSource : ITriggerSource
{
    private readonly string _cronExpression;
    private readonly Lock _gate = new();
    private Timer? _timer;
    private bool _disposed;

    public ScheduledTriggerSource(string cronExpression) => _cronExpression = cronExpression;

    /// <inheritdoc />
    public event EventHandler? Fired;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= new Timer(OnTimer, state: null, Timeout.Infinite, Timeout.Infinite);
            ArmNext();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnTimer(object? state)
    {
        Fired?.Invoke(this, EventArgs.Empty);

        lock (_gate)
        {
            if (!_disposed)
            {
                ArmNext();
            }
        }
    }

    // Schedules the timer for the next cron occurrence. Caller holds _gate.
    private void ArmNext()
    {
        var now = DateTime.UtcNow;
        var next = CronSchedule.NextOccurrenceUtc(_cronExpression, now);
        if (next is null)
        {
            return; // No future occurrence; stays idle.
        }

        var delay = next.Value - now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _timer?.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
