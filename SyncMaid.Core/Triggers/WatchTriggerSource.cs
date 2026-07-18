namespace SyncMaid.Core.Triggers;

/// <summary>
/// Fires when the source directory changes, using a <see cref="FileSystemWatcher"/>.
/// Filesystem events arrive in bursts (a single save can raise several, and one user
/// action often writes several files over seconds), so events are settled: a fire is
/// scheduled <see cref="WatchTrigger.SettleSeconds"/> after the last change and
/// rescheduled if more changes arrive within the window, so one burst syncs as one run.
/// </summary>
public sealed class WatchTriggerSource : ITriggerSource
{
    private readonly TimeSpan _settleWindow;

    private readonly string _path;
    private readonly Func<string, FileSystemWatcher> _watcherFactory;
    private readonly Func<Action, IDebounceTimer> _debounceFactory;
    private readonly Lock _gate = new();
    private readonly TriggerNotifier _notifier = new();
    private FileSystemWatcher? _watcher;
    private IDebounceTimer? _debounce;
    private long _generation;
    private long _debounceArm;
    private bool _started;
    private bool _errorReported;
    private bool _disposed;

    public WatchTriggerSource(
        string path,
        Func<string, FileSystemWatcher>? watcherFactory = null,
        TimeSpan? settleWindow = null)
        : this(path, watcherFactory, callback => new SystemDebounceTimer(callback), settleWindow)
    {
    }

    internal WatchTriggerSource(
        string path,
        Func<string, FileSystemWatcher>? watcherFactory,
        Func<Action, IDebounceTimer> debounceFactory,
        TimeSpan? settleWindow = null)
    {
        _path = path;
        _watcherFactory = watcherFactory ?? (watchPath => new FileSystemWatcher(watchPath));
        _debounceFactory = debounceFactory;
        _settleWindow = settleWindow ?? TimeSpan.FromSeconds(WatchTrigger.DefaultSettleSeconds);
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
        long generation = 0;
        var resumed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _started = true;
            if (_watcher is not null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = true;
                }
                catch
                {
                    // Mirror the fresh-create rollback below: a failed resume must not
                    // leave the source claiming to run with a dead, disabled watcher —
                    // a later Start retries this path.
                    _started = false;
                    throw;
                }

                EnqueueRecoveredLocked();
                resumed = true;
            }
            else
            {
                generation = ++_generation;
            }
        }

        if (resumed)
        {
            _notifier.Drain(OnDeliveryError);
            return;
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
                EnqueueRecoveredLocked();
            }

            _notifier.Drain(OnDeliveryError);
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
            _notifier.Invalidate();
        }

        DisposeDebounce(debounce);

        // Once Stop returns, the source has quiesced: queued notifications were dropped
        // by the epoch bump, and a delivery in flight completes inside this barrier.
        _notifier.WaitForIdle();
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
                created.Change(_settleWindow);
                _debounce = created;
                created = null;
            }
            catch (Exception exception)
            {
                EnqueueErrorLocked(exception);
            }
        }

        DisposeDebounce(previous);
        DisposeDebounce(created);
        _notifier.Drain(OnDeliveryError);
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

                // Decided in order: the error, its recovery, and one fire — the error
                // cancelled any pending debounce, and the OS may already have dropped
                // events before it (buffer overflow), so the changes they carried must
                // still sync; a run over an unchanged tree is a planner no-op.
                EnqueueErrorLocked(e.GetException());
                EnqueueRecoveredLocked();
                _notifier.Enqueue(() => Fired?.Invoke(this, EventArgs.Empty));
            }

            if (installed)
            {
                _notifier.Drain(OnDeliveryError);
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
        IDebounceTimer? completed;
        lock (_gate)
        {
            if (_disposed || !_started || arm != _debounceArm)
            {
                return;
            }

            completed = _debounce;
            _debounce = null;
            _debounceArm++;
            // Decisions under the gate, delivery outside it: the notifier preserves the
            // no-notification-after-Stop guarantee (epoch + Stop's barrier) without
            // subscribers ever running under the state gate.
            _notifier.Enqueue(() => Fired?.Invoke(this, EventArgs.Empty));
            EnqueueRecoveredLocked();
        }

        DisposeDebounce(completed);
        _notifier.Drain(OnDeliveryError);
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

    // Error/Recovered transitions are decided atomically with their enqueue, under the
    // gate — the decided order is the delivered order, so the flag and the last
    // delivered event can never disagree. Their subscribers' own failures are swallowed
    // at delivery; a throwing Fired subscriber surfaces through the drain's callback.
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
        // Never promise recovery on a source the consumer stopped or disposed.
        if (_disposed || !_started || !_errorReported)
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

    // For failures observed outside the gate (cleanup paths); decides, then drains.
    private void ReportError(Exception exception)
    {
        lock (_gate)
        {
            EnqueueErrorLocked(exception);
        }

        _notifier.Drain(OnDeliveryError);
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
            _notifier.Invalidate();
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
        _notifier.WaitForIdle();
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
