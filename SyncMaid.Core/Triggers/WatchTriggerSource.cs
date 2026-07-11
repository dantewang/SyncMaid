namespace SyncMaid.Core.Triggers;

/// <summary>
/// Fires when the source directory changes, using a <see cref="FileSystemWatcher"/>.
/// Filesystem events arrive in bursts (a single save can raise several), so events are
/// debounced: a fire is scheduled a short delay after the last change and rescheduled
/// if more changes arrive within the window.
/// </summary>
public sealed class WatchTriggerSource : ITriggerSource
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly Func<string, FileSystemWatcher> _watcherFactory;
    private readonly Func<Action, IDebounceTimer> _debounceFactory;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private IDebounceTimer? _debounce;
    private long _generation;
    private long _debounceArm;
    private bool _started;
    private bool _errorReported;
    private bool _disposed;

    public WatchTriggerSource(string path, Func<string, FileSystemWatcher>? watcherFactory = null)
        : this(path, watcherFactory, callback => new SystemDebounceTimer(callback))
    {
    }

    internal WatchTriggerSource(
        string path,
        Func<string, FileSystemWatcher>? watcherFactory,
        Func<Action, IDebounceTimer> debounceFactory)
    {
        _path = path;
        _watcherFactory = watcherFactory ?? (watchPath => new FileSystemWatcher(watchPath));
        _debounceFactory = debounceFactory;
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
        long generation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _started = true;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = true;
                ReportRecovered();
                return;
            }

            generation = ++_generation;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = CreateWatcher();
            watcher.EnableRaisingEvents = true;

            lock (_gate)
            {
                if (_disposed || !_started || generation != _generation || _watcher is not null)
                {
                    return;
                }

                _watcher = watcher;
                watcher = null;
                ReportRecovered();
            }
        }
        catch
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _started = false;
                }
            }

            throw;
        }
        finally
        {
            watcher?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        IDebounceTimer? debounce;
        lock (_gate)
        {
            _started = false;
            _generation++;
            _debounceArm++;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
            }

            debounce = _debounce;
            _debounce = null;
        }

        DisposeDebounce(debounce);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        IDebounceTimer? previous = null;
        IDebounceTimer? created = null;
        lock (_gate)
        {
            if (_disposed || !_started || !ReferenceEquals(sender, _watcher))
            {
                return;
            }

            // Each arm captures an immutable token. Disposing a Timer does not retract a
            // callback already queued to the thread pool, so a shared mutable flag cannot
            // distinguish that stale callback from a later lifecycle's new arm.
            var arm = ++_debounceArm;
            previous = _debounce;
            _debounce = null;
            try
            {
                created = _debounceFactory(() => OnDebounceElapsed(arm));
                created.Change(DebounceWindow);
                _debounce = created;
                created = null;
            }
            catch (Exception exception)
            {
                ReportError(exception);
            }
        }

        DisposeDebounce(previous);
        DisposeDebounce(created);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        FileSystemWatcher? failedWatcher;
        IDebounceTimer? debounce;
        long generation;
        lock (_gate)
        {
            if (_disposed || !_started || !ReferenceEquals(sender, _watcher))
            {
                return;
            }

            failedWatcher = _watcher;
            _watcher = null;
            generation = ++_generation;
            _debounceArm++;
            debounce = _debounce;
            _debounce = null;
        }

        DisposeDebounce(debounce);
        try
        {
            failedWatcher.Dispose();
        }
        catch (Exception exception)
        {
            ReportError(new IOException(
                $"The filesystem watcher stopped and its cleanup failed: {exception.Message}",
                new AggregateException(e.GetException(), exception)));
            return;
        }

        FileSystemWatcher? replacement = null;
        Exception? restartFailure = null;
        try
        {
            replacement = CreateWatcher();
            replacement.EnableRaisingEvents = true;

            lock (_gate)
            {
                if (_disposed || !_started || generation != _generation || _watcher is not null)
                {
                    return;
                }

                _watcher = replacement;
                replacement = null;

                // The error cancelled any pending debounce, and the OS may already have
                // dropped events before it (buffer overflow) — the changes they carried
                // must still sync, so fire once after a successful restart; a run over an
                // unchanged tree is a planner no-op. Raised under the gate, like the
                // debounce path, so Stop keeps its no-fire-after-Stop guarantee.
                ReportError(e.GetException());
                ReportRecovered();
                Fired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            restartFailure = exception;
            lock (_gate)
            {
                if (_disposed || !_started || generation != _generation)
                {
                    return;
                }
            }

            ReportError(
                new IOException(
                    $"The filesystem watcher stopped and its restart failed: {exception.Message}",
                    new AggregateException(e.GetException(), exception)));
        }
        finally
        {
            DisposeReplacement(replacement, e.GetException(), restartFailure);
        }
    }

    private void DisposeReplacement(
        FileSystemWatcher? replacement,
        Exception watcherFailure,
        Exception? restartFailure)
    {
        if (replacement is null)
        {
            return;
        }

        try
        {
            replacement.Dispose();
        }
        catch (Exception cleanupFailure)
        {
            var failures = restartFailure is null
                ? new AggregateException(watcherFailure, cleanupFailure)
                : new AggregateException(watcherFailure, restartFailure, cleanupFailure);
            ReportError(new IOException(
                $"The filesystem watcher stopped and replacement cleanup failed: {cleanupFailure.Message}",
                failures));
        }
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = _watcherFactory(_path);
        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size;
        watcher.InternalBufferSize = 64 * 1024;
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    private void OnDebounceElapsed(long arm)
    {
        IDebounceTimer? completed = null;
        try
        {
            lock (_gate)
            {
                if (_disposed || !_started || arm != _debounceArm)
                {
                    return;
                }

                completed = _debounce;
                _debounce = null;
                _debounceArm++;
                // Keep Stop mutually exclusive with delivery: once Stop returns, a callback
                // that was already dequeued cannot notify after it.
                Fired?.Invoke(this, EventArgs.Empty);
                ReportRecovered();
            }
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            DisposeDebounce(completed);
        }
    }

    private void DisposeDebounce(IDebounceTimer? debounce)
    {
        if (debounce is null)
        {
            return;
        }

        try
        {
            debounce.Dispose();
        }
        catch (Exception exception)
        {
            ReportError(new IOException(
                $"The filesystem watcher debounce timer cleanup failed: {exception.Message}",
                exception));
        }
    }

    private void ReportError(Exception exception)
    {
        lock (_gate)
        {
            _errorReported = true;
        }

        try
        {
            Error?.Invoke(exception);
        }
        catch
        {
            // This is a thread-pool boundary; subscriber failures must not escape it either.
        }
    }

    private void ReportRecovered()
    {
        lock (_gate)
        {
            if (!_errorReported)
            {
                return;
            }

            _errorReported = false;
        }

        try
        {
            Recovered?.Invoke();
        }
        catch
        {
            // Recovery observers share the same thread-pool boundary as Error observers.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        FileSystemWatcher? watcher;
        IDebounceTimer? debounce;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            _generation++;
            _debounceArm++;
            watcher = _watcher;
            _watcher = null;
            debounce = _debounce;
            _debounce = null;
        }

        if (watcher is not null)
        {
            try
            {
                watcher.Dispose();
            }
            catch (Exception exception)
            {
                // Watcher disposal can throw for dead network handles (the error-recovery
                // path guards the same call); Dispose must not throw, and the debounce
                // below must still be cleaned up.
                ReportError(new IOException(
                    $"The filesystem watcher cleanup failed: {exception.Message}", exception));
            }
        }

        DisposeDebounce(debounce);
    }

    internal interface IDebounceTimer : IDisposable
    {
        void Change(TimeSpan dueTime);
    }

    private sealed class SystemDebounceTimer : IDebounceTimer
    {
        private readonly Timer _timer;

        public SystemDebounceTimer(Action callback) =>
            _timer = new Timer(_ => callback(), state: null, Timeout.Infinite, Timeout.Infinite);

        public void Change(TimeSpan dueTime) =>
            _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

        public void Dispose() => _timer.Dispose();
    }
}
