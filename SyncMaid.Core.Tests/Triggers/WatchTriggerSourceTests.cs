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
            source.Error += exception => reported = exception;
            source.Start();

            Assert.True(Assert.Single(created).EnableRaisingEvents);
            created[0].RaiseError(new InternalBufferOverflowException("overflow"));

            Assert.Equal(2, created.Count);
            Assert.True(created[1].EnableRaisingEvents);
            Assert.Null(reported);
        }
        finally
        {
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

    private sealed class TestFileSystemWatcher(string path) : FileSystemWatcher(path)
    {
        public void RaiseError(Exception exception) => OnError(new ErrorEventArgs(exception));
    }
}
