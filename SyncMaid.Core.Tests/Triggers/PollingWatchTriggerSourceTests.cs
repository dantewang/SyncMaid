using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class PollingWatchTriggerSourceTests
{
    private const string Root = @"\\nas\share\src";

    private static PollingWatchTriggerSource Source(InMemoryFileSystem fs) =>
        // A fake timer keeps tests deterministic; snapshots are advanced by PollOnce.
        new(fs, Root, TimeSpan.FromHours(1), _ => new FakeTimer());

    [Fact]
    public void Fires_when_a_file_is_added()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);
        var fired = 0;
        source.Fired += (_, _) => fired++;

        source.Start(); // captures the initial snapshot
        Assert.False(source.PollOnce()); // nothing changed yet

        fs.AddFile($@"{Root}\b.txt", "b");
        Assert.True(source.PollOnce()); // new file detected

        Assert.Equal(1, fired);
        Assert.False(source.PollOnce()); // settled — no repeat fire
    }

    [Fact]
    public void Fires_when_a_file_changes_or_is_removed()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        fs.AddFile($@"{Root}\b.txt", "b");
        using var source = Source(fs);
        source.Start();
        Assert.False(source.PollOnce()); // asynchronous first tick establishes the baseline

        fs.AddFile($@"{Root}\a.txt", "a-changed-longer"); // size/stamp changes
        Assert.True(source.PollOnce());

        fs.DeleteFile($@"{Root}\b.txt");
        Assert.True(source.PollOnce());

        Assert.False(source.PollOnce());
    }

    [Fact]
    public void Fires_when_an_empty_directory_appears_or_vanishes()
    {
        // Mirror replicates directory structure, so a structure-only change must
        // trigger a run even though no file stamp moved.
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);
        source.Start();
        Assert.False(source.PollOnce());

        fs.EnsureDirectory($@"{Root}\empty");
        Assert.True(source.PollOnce());

        fs.DeleteEmptyDirectory($@"{Root}\empty");
        Assert.True(source.PollOnce());

        Assert.False(source.PollOnce());
    }

    [Fact]
    public void First_poll_after_start_takes_a_baseline_without_firing()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);

        source.Start();
        Assert.False(source.PollOnce());
    }

    [Fact]
    public async Task Start_arms_an_immediate_timer_without_touching_the_filesystem()
    {
        var fileSystem = new BlockingFileSystem();
        FakeTimer? timer = null;
        using var source = new PollingWatchTriggerSource(
            fileSystem,
            Root,
            TimeSpan.FromSeconds(5),
            callback => timer = new FakeTimer(callback));

        var start = Task.Run(source.Start);
        try
        {
            await start.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(fileSystem.EnumerationStarted.IsSet);
            Assert.Equal(TimeSpan.Zero, timer!.DueTime);
            Assert.Equal(TimeSpan.FromSeconds(5), timer.Period);
        }
        finally
        {
            fileSystem.ReleaseEnumeration.Set();
            await start.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void Changes_made_while_stopped_are_absorbed_on_resume()
    {
        // A Move run deletes from the watched source with the trigger suppressed; resuming
        // must re-baseline so the run's own deletions don't fire a pointless follow-up run.
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        fs.AddFile($@"{Root}\b.txt", "b");
        using var source = Source(fs);
        source.Start();

        source.Stop();
        fs.DeleteFile($@"{Root}\a.txt"); // the run's own change, made while suppressed
        source.Start();

        Assert.False(source.PollOnce()); // absorbed — no fire for our own deletions

        fs.AddFile($@"{Root}\c.txt", "c");
        Assert.True(source.PollOnce());  // a genuine later change still fires
    }

    [Fact]
    public void Enumeration_failure_keeps_the_last_good_snapshot_and_next_poll_resumes()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        fs.AddFile($@"{Root}\b.txt", "b");
        using var source = Source(fs);
        var fires = 0;
        Exception? reported = null;
        var errors = 0;
        var recoveries = 0;
        source.Fired += (_, _) => fires++;
        source.Error += exception =>
        {
            errors++;
            reported = exception;
        };
        source.Recovered += () => recoveries++;
        source.Start();
        fs.FailEnumerationAfter = 1;
        fs.EnumerationFailuresRemaining = 2;

        var changed = true;
        var escaped = Record.Exception(() => changed = source.PollOnce());

        Assert.Null(escaped);
        Assert.False(changed);
        Assert.Equal(0, fires);
        Assert.IsType<IOException>(reported);
        Assert.False(source.PollOnce()); // repeated failure is contained without log-spam
        Assert.Equal(1, errors);
        Assert.False(source.PollOnce()); // unchanged successful snapshot must not spuriously fire
        Assert.Equal(1, recoveries);

        fs.AddFile($@"{Root}\c.txt", "c");
        Assert.True(source.PollOnce());
        Assert.Equal(1, fires);
    }

    // PollOnce runs on a raw timer callback, so the boundary must contain every exception
    // type — a malformed configured path throws ArgumentException, not an I/O exception,
    // and an escape would take down the whole process.
    [Fact]
    public void Non_io_poll_failures_are_contained_and_reported_like_io_ones()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);
        var fires = 0;
        Exception? reported = null;
        var recoveries = 0;
        source.Fired += (_, _) => fires++;
        source.Error += exception => reported = exception;
        source.Recovered += () => recoveries++;
        source.Start();
        fs.EnumerationFailure = () => new ArgumentException("Simulated malformed watch path.");
        fs.EnumerationFailuresRemaining = 1;

        var changed = true;
        var escaped = Record.Exception(() => changed = source.PollOnce());

        Assert.Null(escaped);
        Assert.False(changed);
        Assert.IsType<ArgumentException>(reported);

        Assert.False(source.PollOnce()); // recovery takes a baseline without a spurious fire
        Assert.Equal(1, recoveries);
        fs.AddFile($@"{Root}\b.txt", "b");
        Assert.True(source.PollOnce()); // and a genuine later change still fires
        Assert.Equal(1, fires);
    }

    // An unplugged share is now a DirectoryNotFoundException from enumeration, so the
    // existing poll boundary turns it into the error badge — and its return into the
    // recovery — with no spurious fire on the re-baseline.
    [Fact]
    public void Missing_watch_root_reports_error_and_recovers_when_it_returns()
    {
        var fs = new InMemoryFileSystem(); // the watched root does not exist yet
        using var source = Source(fs);
        var fires = 0;
        Exception? reported = null;
        var recoveries = 0;
        source.Fired += (_, _) => fires++;
        source.Error += exception => reported = exception;
        source.Recovered += () => recoveries++;
        source.Start();

        Assert.False(source.PollOnce());
        Assert.IsType<DirectoryNotFoundException>(reported);
        Assert.False(source.PollOnce()); // still gone — reported once, no spam
        Assert.Equal(0, recoveries);

        fs.AddFile($@"{Root}\a.txt", "a"); // the share comes back
        Assert.False(source.PollOnce());   // recovery takes a baseline without firing
        Assert.Equal(1, recoveries);
        Assert.Equal(0, fires);

        fs.AddFile($@"{Root}\b.txt", "b");
        Assert.True(source.PollOnce());    // genuine changes fire again
        Assert.Equal(1, fires);
    }

    // A throwing Fired subscriber previously escaped PollOnce bare (delivered outside
    // any try, on a raw timer callback) and killed the process. The drain contains it
    // and folds it into the once-until-recovered error reporting.
    [Fact]
    public void A_throwing_fired_subscriber_is_contained_and_reported()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);
        Exception? reported = null;
        var recoveries = 0;
        source.Fired += (_, _) => throw new InvalidOperationException("handler failed");
        source.Error += exception => reported = exception;
        source.Recovered += () => recoveries++;
        source.Start();
        Assert.False(source.PollOnce()); // baseline

        fs.AddFile($@"{Root}\b.txt", "b");
        var escaped = Record.Exception(() => source.PollOnce());

        Assert.Null(escaped);
        Assert.IsType<InvalidOperationException>(reported);

        Assert.False(source.PollOnce()); // the next successful poll clears the error state
        Assert.Equal(1, recoveries);
    }

    private sealed class FakeTimer(Action? callback = null) : PollingWatchTriggerSource.IPollingTimer
    {
        public TimeSpan DueTime { get; private set; }
        public TimeSpan Period { get; private set; }

        public void Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
        }

        public void Fire() => callback?.Invoke();

        public void Dispose()
        {
        }
    }

    private sealed class BlockingFileSystem : IFileSystem
    {
        public ManualResetEventSlim EnumerationStarted { get; } = new();
        public ManualResetEventSlim ReleaseEnumeration { get; } = new();

        public IEnumerable<string> EnumerateFiles(string root)
        {
            EnumerationStarted.Set();
            ReleaseEnumeration.Wait();
            yield break;
        }

        // The poll's snapshot also lists directories; only the file walk blocks.
        public IEnumerable<string> EnumerateDirectories(string root) => [];

        public bool FileExists(string path) => throw new NotSupportedException();
        public FileStamp GetStamp(string path) => throw new NotSupportedException();
        public byte[] ReadAllBytes(string path) => throw new NotSupportedException();
        public void WriteAllBytes(string path, byte[] contents) => throw new NotSupportedException();
        public void DeleteFile(string path) => throw new NotSupportedException();
        public void Recycle(string path) => throw new NotSupportedException();
        public void EnsureDirectory(string path) => throw new NotSupportedException();
        public void DeleteEmptyDirectory(string path) => throw new NotSupportedException();
        public Stream OpenRead(string path) => throw new NotSupportedException();
        public Stream CreateWriteThrough(string path) => throw new NotSupportedException();
        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new NotSupportedException();
        public void Replace(string sourcePath, string destinationPath) => throw new NotSupportedException();
        public long GetAvailableFreeSpace(string path) => throw new NotSupportedException();
    }
}
