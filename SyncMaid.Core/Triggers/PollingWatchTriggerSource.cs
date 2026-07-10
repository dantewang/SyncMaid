using SyncMaid.Core.IO;

namespace SyncMaid.Core.Triggers;

/// <summary>
/// A watch trigger for sources where <see cref="System.IO.FileSystemWatcher"/> is
/// unreliable — mounted network paths (UNC / mapped drives), where events are frequently
/// missed. Instead of OS change events it periodically snapshots the source tree (relative
/// path → <see cref="FileStamp"/>) and fires when the snapshot changes.
/// </summary>
public sealed class PollingWatchTriggerSource : ITriggerSource
{
    /// <summary>Default poll interval — a balance between latency and network chatter.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly IFileSystem _fileSystem;
    private readonly string _path;
    private readonly TimeSpan _interval;
    private readonly Func<Action, IPollingTimer> _timerFactory;
    private readonly Lock _gate = new();

    private IPollingTimer? _timer;
    private Dictionary<string, FileStamp> _snapshot = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasBaseline;
    private bool _failureReported;
    private bool _disposed;

    public PollingWatchTriggerSource(IFileSystem fileSystem, string path, TimeSpan? interval = null)
        : this(fileSystem, path, interval, callback => new SystemPollingTimer(callback))
    {
    }

    internal PollingWatchTriggerSource(
        IFileSystem fileSystem,
        string path,
        TimeSpan? interval,
        Func<Action, IPollingTimer> timerFactory)
    {
        _fileSystem = fileSystem;
        _path = path;
        _interval = interval ?? DefaultInterval;
        _timerFactory = timerFactory;
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
            if (_disposed || _timer is not null)
            {
                return;
            }

            _hasBaseline = false;
            var timer = _timerFactory(() => PollOnce());
            try
            {
                timer.Change(TimeSpan.Zero, _interval);
                _timer = timer;
            }
            catch
            {
                timer.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _hasBaseline = false;
        }
    }

    /// <summary>
    /// Takes one snapshot and fires if it differs from the previous one. The first successful
    /// poll after each <see cref="Start"/> establishes a baseline without firing. Called by the
    /// timer; exposed so a change can be detected deterministically in tests without waiting on
    /// the clock. Returns true when a change was detected.
    /// </summary>
    public bool PollOnce()
    {
        Exception? failure = null;
        var reportFailure = false;
        var recovered = false;
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            Dictionary<string, FileStamp>? current = null;
            try
            {
                current = Snapshot();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failure = exception;
            }

            if (failure is not null)
            {
                reportFailure = !_failureReported;
                _failureReported = true;
            }
            else
            {
                recovered = _failureReported;
                _failureReported = false;
                if (!_hasBaseline)
                {
                    _snapshot = current!;
                    _hasBaseline = true;
                }
                else if (Differs(current!, _snapshot))
                {
                    _snapshot = current!;
                    changed = true;
                }
            }
        }

        if (reportFailure)
        {
            ReportError(failure!);
        }

        if (recovered)
        {
            ReportRecovered();
        }

        if (changed)
        {
            // Notify outside the lock so a handler that runs a sync can't deadlock on it.
            Fired?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    private void ReportError(Exception exception)
    {
        try
        {
            Error?.Invoke(exception);
        }
        catch
        {
            // This is a timer callback boundary; error subscribers cannot be allowed to escape it.
        }
    }

    private void ReportRecovered()
    {
        try
        {
            Recovered?.Invoke();
        }
        catch
        {
            // Recovery notification shares the timer callback boundary.
        }
    }

    private Dictionary<string, FileStamp> Snapshot()
    {
        var snapshot = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in _fileSystem.EnumerateFiles(_path))
        {
            try
            {
                snapshot[relative] = _fileSystem.GetStamp(RelativePaths.Join(_path, relative));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file vanished between listing and stamping, or is momentarily locked;
                // skip it this round — the next poll will pick up the settled state.
            }
        }

        return snapshot;
    }

    private static bool Differs(Dictionary<string, FileStamp> a, Dictionary<string, FileStamp> b)
    {
        if (a.Count != b.Count)
        {
            return true;
        }

        foreach (var (path, stamp) in a)
        {
            if (!b.TryGetValue(path, out var other) || other != stamp)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _hasBaseline = false;
        }
    }

    internal interface IPollingTimer : IDisposable
    {
        void Change(TimeSpan dueTime, TimeSpan period);
    }

    private sealed class SystemPollingTimer : IPollingTimer
    {
        private readonly Timer _timer;

        public SystemPollingTimer(Action callback)
        {
            _timer = new Timer(_ => callback(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Change(TimeSpan dueTime, TimeSpan period) => _timer.Change(dueTime, period);

        public void Dispose() => _timer.Dispose();
    }
}
