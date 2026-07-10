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
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public WatchTriggerSource(string path, Func<string, FileSystemWatcher>? watcherFactory = null)
    {
        _path = path;
        _watcherFactory = watcherFactory ?? (watchPath => new FileSystemWatcher(watchPath));
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

            if (_watcher is not null)
            {
                // Resume after Stop(): the watcher survives a stop with events disabled, so
                // re-enable rather than early-return (which would make resume a silent no-op).
                _watcher.EnableRaisingEvents = true;
                return;
            }

            _debounce ??= new Timer(OnDebounceElapsed, state: null, Timeout.Infinite, Timeout.Infinite);
            _watcher = CreateWatcher();
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
            }

            _debounce?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            // Restart the debounce window; we only fire once the dust settles.
            _debounce?.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Exception? restartFailure = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                DisposeWatcher();
                _watcher = CreateWatcher();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception)
            {
                try
                {
                    DisposeWatcher();
                }
                catch
                {
                    // Preserve the restart failure; the watcher is already unusable.
                }

                restartFailure = new IOException(
                    $"The filesystem watcher stopped and its restart failed: {exception.Message}",
                    new AggregateException(e.GetException(), exception));
            }
        }

        if (restartFailure is not null)
        {
            Error?.Invoke(restartFailure);
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
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    private void DisposeWatcher()
    {
        var watcher = _watcher;
        _watcher = null;
        watcher?.Dispose();
    }

    private void OnDebounceElapsed(object? state) => Fired?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            DisposeWatcher();

            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
