using SyncMaid.Core.IO;

namespace SyncMaid.Core.Triggers;

/// <summary>
/// A watch trigger for sources where <see cref="System.IO.FileSystemWatcher"/> is
/// unreliable — mounted network paths (UNC / mapped drives), where events are frequently
/// missed. Instead of OS change events it periodically snapshots the source tree (relative
/// path → <see cref="FileStamp"/>, plus the directory set, so an empty directory appearing
/// or vanishing counts as a change — Mirror replicates structure) and fires when the
/// snapshot changes.
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
    private readonly TriggerNotifier _notifier = new();

    private IPollingTimer? _timer;
    private TreeSnapshot _snapshot = TreeSnapshot.Empty;
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
            _notifier.Invalidate();
        }

        // Once Stop returns, the source has quiesced: queued notifications were dropped
        // by the epoch bump, and a delivery in flight completes inside this barrier.
        _notifier.WaitForIdle();
    }

    /// <summary>
    /// Takes one snapshot and fires if it differs from the previous one. The first successful
    /// poll after each <see cref="Start"/> establishes a baseline without firing. Called by the
    /// timer; exposed so a change can be detected deterministically in tests without waiting on
    /// the clock. Returns true when a change was detected.
    /// </summary>
    public bool PollOnce()
    {
        // Decisions happen under the gate and enqueue their notifications; the notifier
        // delivers them outside it, in decided order, and drops them if Stop/Dispose
        // lands first. A throwing Fired subscriber is contained by the drain and folded
        // into the same once-until-recovered error reporting as a failed snapshot.
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            TreeSnapshot? current = null;
            Exception? failure = null;
            try
            {
                current = Snapshot();
            }
            catch (Exception exception)
            {
                // This is a timer-callback boundary: anything escaping here (a malformed
                // configured path throwing ArgumentException, not just I/O faults) would be
                // an unhandled thread-pool exception and kill the process. Deferring the
                // baseline off Start() also moved the first walk out of the consumer's
                // start-failure handling, so this catch is the only net.
                failure = exception;
            }

            if (failure is not null)
            {
                EnqueueErrorOnceLocked(failure);
            }
            else
            {
                EnqueueRecoveredLocked();
                if (!_hasBaseline)
                {
                    _snapshot = current!;
                    _hasBaseline = true;
                }
                else if (current!.Differs(_snapshot))
                {
                    _snapshot = current!;
                    changed = true;
                    _notifier.Enqueue(() => Fired?.Invoke(this, EventArgs.Empty));
                }
            }
        }

        _notifier.Drain(OnDeliveryError);
        return changed;
    }

    // Reported once until the next successful poll, so a persistently failing share
    // does not spam the consumer. Subscriber failures are swallowed at delivery.
    private void EnqueueErrorOnceLocked(Exception exception)
    {
        if (_failureReported)
        {
            return;
        }

        _failureReported = true;
        _notifier.Enqueue(() =>
        {
            try
            {
                Error?.Invoke(exception);
            }
            catch
            {
                // Subscriber failures must not escape the timer callback boundary.
            }
        });
    }

    private void EnqueueRecoveredLocked()
    {
        if (!_failureReported)
        {
            return;
        }

        _failureReported = false;
        _notifier.Enqueue(() =>
        {
            try
            {
                Recovered?.Invoke();
            }
            catch
            {
                // Recovery notification shares the timer callback boundary.
            }
        });
    }

    private void OnDeliveryError(Exception exception)
    {
        lock (_gate)
        {
            EnqueueErrorOnceLocked(exception);
        }
    }

    private TreeSnapshot Snapshot()
    {
        var files = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in _fileSystem.EnumerateFiles(_path))
        {
            try
            {
                files[relative] = _fileSystem.GetStamp(RelativePaths.Join(_path, relative));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file vanished between listing and stamping, or is momentarily locked;
                // skip it this round — the next poll will pick up the settled state.
            }
        }

        var directories = new HashSet<string>(
            _fileSystem.EnumerateDirectories(_path), StringComparer.OrdinalIgnoreCase);
        return new TreeSnapshot(files, directories);
    }

    private sealed class TreeSnapshot
    {
        private readonly Dictionary<string, FileStamp> _files;
        private readonly HashSet<string> _directories;

        public TreeSnapshot(Dictionary<string, FileStamp> files, HashSet<string> directories)
        {
            _files = files;
            _directories = directories;
        }

        public static readonly TreeSnapshot Empty = new(
            new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public bool Differs(TreeSnapshot other)
        {
            if (_files.Count != other._files.Count || !_directories.SetEquals(other._directories))
            {
                return true;
            }

            foreach (var (path, stamp) in _files)
            {
                if (!other._files.TryGetValue(path, out var otherStamp) || otherStamp != stamp)
                {
                    return true;
                }
            }

            return false;
        }
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
            _notifier.Invalidate();
        }

        _notifier.WaitForIdle();
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
