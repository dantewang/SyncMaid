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
            using var source = new WatchTriggerSource(directory);
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
            using var source = new WatchTriggerSource(directory);
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

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncmaid-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeDebounceTimer(Action callback) : WatchTriggerSource.IDebounceTimer
    {
        public void Change(TimeSpan dueTime) { }
        public void Fire() => callback();
        public void Dispose() { }
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
