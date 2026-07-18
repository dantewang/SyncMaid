using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class WatchTriggerSourceTests
{
    [Fact]
    public void Watcher_error_recreates_and_reenables_the_watcher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var created = new List<TestFileSystemWatcher>();

        try
        {
            using var source = new WatchTriggerSource(directory, path =>
            {
                var watcher = new TestFileSystemWatcher(path);
                created.Add(watcher);
                return watcher;
            });
            Exception? reported = null;
            var recoveries = 0;
            source.Error += exception => reported = exception;
            source.Recovered += () => recoveries++;
            source.Start();

            Assert.True(Assert.Single(created).EnableRaisingEvents);
            Assert.Equal(64 * 1024, created[0].InternalBufferSize);
            created[0].RaiseError(new InternalBufferOverflowException("overflow"));

            Assert.Equal(2, created.Count);
            Assert.True(created[1].EnableRaisingEvents);
            Assert.Equal(64 * 1024, created[1].InternalBufferSize);
            Assert.IsType<InternalBufferOverflowException>(reported);
            Assert.Equal(1, recoveries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Watcher_error_while_stopped_does_not_restart_or_reenable()
    {
        var directory = NewDirectory();
        var created = new List<TestFileSystemWatcher>();
        try
        {
            using var source = new WatchTriggerSource(directory, path =>
            {
                var watcher = new TestFileSystemWatcher(path);
                created.Add(watcher);
                return watcher;
            });
            source.Start();
            source.Stop();

            created[0].RaiseError(new IOException("late error"));

            Assert.Single(created);
            Assert.False(created[0].EnableRaisingEvents);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Dequeued_debounce_and_throwing_handlers_are_contained_after_stop()
    {
        var directory = NewDirectory();
        FakeDebounceTimer? timer = null;
        var watcher = new TestFileSystemWatcher(directory);
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => watcher,
                callback => timer = new FakeDebounceTimer(callback));
            var fires = 0;
            Exception? reported = null;
            source.Fired += (_, _) =>
            {
                fires++;
                throw new InvalidOperationException("handler failed");
            };
            source.Error += exception => reported = exception;
            source.Start();
            watcher.RaiseChanged();

            var escaped = Record.Exception(timer!.Fire);

            Assert.Null(escaped);
            Assert.Equal(1, fires);
            Assert.IsType<InvalidOperationException>(reported);

            watcher.RaiseChanged();
            source.Stop();
            escaped = Record.Exception(timer.Fire); // callback was already dequeued
            Assert.Null(escaped);
            Assert.Equal(1, fires);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Debounce_dequeued_before_stop_cannot_fire_after_a_subsequent_start()
    {
        var directory = NewDirectory();
        var timers = new List<FakeDebounceTimer>();
        var watcher = new TestFileSystemWatcher(directory);
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => watcher,
                callback =>
                {
                    var timer = new FakeDebounceTimer(callback);
                    timers.Add(timer);
                    return timer;
                });
            var fires = 0;
            source.Fired += (_, _) => fires++;
            source.Start();
            watcher.RaiseChanged();
            var staleTimer = Assert.Single(timers);

            source.Stop();
            source.Start();
            watcher.RaiseChanged();
            Assert.Equal(2, timers.Count);

            staleTimer.Fire(); // callback from the arm that preceded Stop()
            Assert.Equal(0, fires);

            timers[1].Fire();
            Assert.Equal(1, fires);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_blocked_restart_does_not_block_dispose_or_resurrect_the_watcher()
    {
        var directory = NewDirectory();
        var initial = new TestFileSystemWatcher(directory);
        var restarted = new TestFileSystemWatcher(directory);
        var restartEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRestart = new ManualResetEventSlim();
        var attempts = 0;
        var source = new WatchTriggerSource(directory, _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                return initial;
            }

            restartEntered.TrySetResult();
            releaseRestart.Wait();
            return restarted;
        });

        try
        {
            source.Start();
            var error = Task.Run(() => initial.RaiseError(new IOException("disconnected")));
            await restartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Task.Run(source.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
            releaseRestart.Set();
            await error.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(restarted.IsDisposed);
            Assert.False(restarted.EnableRaisingEvents);
        }
        finally
        {
            releaseRestart.Set();
            source.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Replacement_cleanup_failure_is_contained_when_stop_invalidates_a_restart()
    {
        var directory = NewDirectory();
        var initial = new TestFileSystemWatcher(directory);
        var replacement = new TestFileSystemWatcher(directory) { ThrowOnDispose = true };
        var restartEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRestart = new ManualResetEventSlim();
        var attempts = 0;
        using var source = new WatchTriggerSource(directory, _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                return initial;
            }

            restartEntered.TrySetResult();
            releaseRestart.Wait();
            return replacement;
        });
        Exception? reported = null;
        source.Error += exception => reported = exception;

        try
        {
            source.Start();
            var errorCallback = Task.Run(() => initial.RaiseError(new IOException("disconnected")));
            await restartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            source.Stop();
            releaseRestart.Set();

            var escaped = await Record.ExceptionAsync(
                async () => await errorCallback.WaitAsync(TimeSpan.FromSeconds(1)));

            Assert.Null(escaped);
            Assert.IsType<IOException>(reported);
        }
        finally
        {
            releaseRestart.Set();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Failed_watcher_restart_reports_the_error()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var created = new TestFileSystemWatcher(directory);
        var attempts = 0;

        try
        {
            using var source = new WatchTriggerSource(directory, _ =>
            {
                attempts++;
                return attempts == 1 ? created : throw new IOException("restart failed");
            });
            Exception? reported = null;
            source.Error += exception => reported = exception;
            source.Start();

            created.RaiseError(new IOException("watcher stopped"));

            Assert.Equal(2, attempts);
            Assert.NotNull(reported);
            Assert.Contains("restart", reported.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Armed_debounce_cannot_fire_or_report_recovery_after_restart_failure()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory);
        FakeDebounceTimer? timer = null;
        var attempts = 0;
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => ++attempts == 1 ? watcher : throw new IOException("restart failed"),
                callback => timer = new FakeDebounceTimer(callback));
            var fires = 0;
            var recoveries = 0;
            source.Fired += (_, _) => fires++;
            source.Recovered += () => recoveries++;
            source.Start();
            watcher.RaiseChanged();

            watcher.RaiseError(new IOException("watcher stopped"));
            timer!.Fire();

            Assert.Equal(0, fires);
            Assert.Equal(0, recoveries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A watcher error cancels the pending debounce, and the OS may have dropped events
    // before it — after a successful restart the source must fire once so the changes
    // those events carried still sync, instead of being silently lost.
    [Fact]
    public void Successful_restart_after_an_error_fires_so_pending_changes_still_sync()
    {
        var directory = NewDirectory();
        var initial = new TestFileSystemWatcher(directory);
        FakeDebounceTimer? timer = null;
        var attempts = 0;
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => ++attempts == 1 ? initial : new TestFileSystemWatcher(directory),
                callback => timer = new FakeDebounceTimer(callback));
            var fires = 0;
            var recoveries = 0;
            source.Fired += (_, _) => fires++;
            source.Recovered += () => recoveries++;
            source.Start();
            initial.RaiseChanged(); // a real change is pending in the debounce window

            initial.RaiseError(new IOException("watcher stopped"));

            Assert.Equal(1, fires);      // the pending change is not lost
            Assert.Equal(1, recoveries); // and the badge still clears
            timer!.Fire();               // the cancelled arm stays cancelled — no double fire
            Assert.Equal(1, fires);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The configured settle window is what arms the debounce — the SyncBack-style
    // "wait for no changes before running" knob, per task.
    [Fact]
    public void The_configured_settle_window_arms_the_debounce()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory);
        FakeDebounceTimer? timer = null;
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => watcher,
                callback => timer = new FakeDebounceTimer(callback),
                settleWindow: TimeSpan.FromSeconds(42));
            source.Start();
            watcher.RaiseChanged();

            Assert.Equal(TimeSpan.FromSeconds(42), timer!.ArmedFor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Dispose_contains_watcher_cleanup_failures_and_still_disposes_the_debounce()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory) { ThrowOnDispose = true };
        FakeDebounceTimer? timer = null;
        try
        {
            var source = new WatchTriggerSource(
                directory, _ => watcher, callback => timer = new FakeDebounceTimer(callback));
            source.Start();
            watcher.RaiseChanged(); // arms a debounce that Dispose must still clean up

            var escaped = Record.Exception(source.Dispose);

            Assert.Null(escaped);
            Assert.True(timer!.Disposed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Failed_watcher_disposal_is_contained_at_the_error_callback_boundary()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory) { ThrowOnDispose = true };
        try
        {
            using var source = new WatchTriggerSource(directory, _ => watcher);
            Exception? reported = null;
            source.Error += exception => reported = exception;
            source.Start();

            var escaped = Record.Exception(() => watcher.RaiseError(new IOException("watcher stopped")));

            Assert.Null(escaped);
            Assert.IsType<IOException>(reported);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fires_when_a_file_appears_in_the_watched_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            // A short real settle keeps this real-timer integration test fast.
            using var source = new WatchTriggerSource(
                directory, settleWindow: TimeSpan.FromMilliseconds(250));
            var fired = new TaskCompletionSource();
            source.Fired += (_, _) => fired.TrySetResult();
            source.Start();

            // Give the watcher a moment to arm before writing.
            await Task.Delay(100);
            await File.WriteAllTextAsync(Path.Combine(directory, "new.txt"), "hi");

            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(ReferenceEquals(completed, fired.Task), "Watcher did not fire within the timeout.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Does_not_fire_after_stop()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var source = new WatchTriggerSource(directory);
            var fireCount = 0;
            source.Fired += (_, _) => Interlocked.Increment(ref fireCount);
            source.Start();
            source.Stop();

            await Task.Delay(100);
            await File.WriteAllTextAsync(Path.Combine(directory, "new.txt"), "hi");
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(0, fireCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fires_again_after_stop_then_start()
    {
        // Resume-after-suppression (used around a sync run): Start() after Stop() must
        // re-enable the existing watcher, not silently no-op.
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            // A short real settle keeps this real-timer integration test fast.
            using var source = new WatchTriggerSource(
                directory, settleWindow: TimeSpan.FromMilliseconds(250));
            var fired = new TaskCompletionSource();
            source.Fired += (_, _) => fired.TrySetResult();

            source.Start();
            source.Stop();
            source.Start();

            await Task.Delay(100);
            await File.WriteAllTextAsync(Path.Combine(directory, "new.txt"), "hi");

            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(ReferenceEquals(completed, fired.Task), "Watcher did not fire after resume.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Delivery happens outside the state gate: while a Fired subscriber is still
    // running, watcher events must keep arming debounces instead of queuing up
    // behind the handler.
    [Fact]
    public async Task A_blocking_fired_subscriber_does_not_block_watcher_events()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory);
        var timers = new List<FakeDebounceTimer>();
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => watcher,
                callback =>
                {
                    var timer = new FakeDebounceTimer(callback);
                    timers.Add(timer);
                    return timer;
                });
            var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseHandler = new ManualResetEventSlim();
            source.Fired += (_, _) =>
            {
                handlerEntered.TrySetResult();
                releaseHandler.Wait();
            };
            source.Start();
            watcher.RaiseChanged();

            var delivery = Task.Run(timers[0].Fire);
            await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            // With the handler still blocked, a new change must arm a new debounce.
            var changed = Task.Run(watcher.RaiseChanged);
            await changed.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(2, timers.Count);

            releaseHandler.Set();
            await delivery.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A failed resume must roll back _started, so the source does not act running with
    // a dead, disabled watcher — and a later Start can retry cleanly.
    [Fact]
    public void Resume_failure_rolls_back_so_the_source_does_not_act_started()
    {
        var directory = NewDirectory();
        var watcher = new TestFileSystemWatcher(directory);
        var timers = new List<FakeDebounceTimer>();
        try
        {
            using var source = new WatchTriggerSource(
                directory,
                _ => watcher,
                callback =>
                {
                    var timer = new FakeDebounceTimer(callback);
                    timers.Add(timer);
                    return timer;
                });
            source.Start();
            source.Stop();

            Directory.Delete(directory, recursive: true);
            Assert.NotNull(Record.Exception(source.Start)); // resume fails: folder is gone

            watcher.RaiseChanged();                         // a stray event must not arm anything
            Assert.Empty(timers);

            Directory.CreateDirectory(directory);
            Assert.Null(Record.Exception(source.Start));    // rollback lets the retry succeed
            watcher.RaiseChanged();
            Assert.Single(timers);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeDebounceTimer(Action callback) : WatchTriggerSource.IDebounceTimer
    {
        public bool Disposed { get; private set; }
        public TimeSpan? ArmedFor { get; private set; }
        public void Change(TimeSpan dueTime) => ArmedFor = dueTime;
        public void Fire() => callback();
        public void Dispose() => Disposed = true;
    }

    private sealed class TestFileSystemWatcher(string path) : FileSystemWatcher(path)
    {
        public bool IsDisposed { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public void RaiseChanged() => OnChanged(new FileSystemEventArgs(
            WatcherChangeTypes.Changed, Path, "changed.txt"));
        public void RaiseError(Exception exception) => OnError(new ErrorEventArgs(exception));

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            var throwAfterDisposal = ThrowOnDispose;
            ThrowOnDispose = false;
            base.Dispose(disposing);
            if (throwAfterDisposal)
            {
                throw new InvalidOperationException("Simulated watcher disposal failure.");
            }
        }
    }
}
