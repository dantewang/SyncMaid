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
            _debounce ??= _debounceFactory(OnDebounceElapsed);
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
        lock (_gate)
        {
            _started = false;
            _generation++;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
            }

            _debounce?.Change(Timeout.InfiniteTimeSpan);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_started || !ReferenceEquals(sender, _watcher))
            {
                return;
            }

            // Restart the debounce window; we only fire once the dust settles.
            _debounce?.Change(DebounceWindow);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        FileSystemWatcher? failedWatcher;
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
        }

        failedWatcher.Dispose();

        FileSystemWatcher? replacement = null;
        try
        {
            replacement = CreateWatcher();
            replacement.EnableRaisingEvents = true;

            var installed = false;
            lock (_gate)
            {
                if (_disposed || !_started || generation != _generation || _watcher is not null)
                {
                    return;
                }

                _watcher = replacement;
                replacement = null;
                installed = true;
            }

            if (installed)
            {
                ReportError(e.GetException());
                ReportRecovered();
            }
        }
        catch (Exception exception)
        {
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
            replacement?.Dispose();
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

    private void OnDebounceElapsed()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !_started)
                {
                    return;
                }

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
            watcher = _watcher;
            _watcher = null;
            debounce = _debounce;
            _debounce = null;
        }

        watcher?.Dispose();
        debounce?.Dispose();
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
