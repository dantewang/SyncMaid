using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class PollingWatchTriggerSourceTests
{
    private const string Root = @"\\nas\share\src";

    private static PollingWatchTriggerSource Source(InMemoryFileSystem fs) =>
        // A long interval so the background timer never fires during the test; we poll by hand.
        new(fs, Root, TimeSpan.FromHours(1));

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

        fs.AddFile($@"{Root}\a.txt", "a-changed-longer"); // size/stamp changes
        Assert.True(source.PollOnce());

        fs.DeleteFile($@"{Root}\b.txt");
        Assert.True(source.PollOnce());

        Assert.False(source.PollOnce());
    }

    [Fact]
    public void Does_not_fire_before_start_takes_a_baseline()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{Root}\a.txt", "a");
        using var source = Source(fs);

        // Without Start(), the baseline is empty; the first poll sees the existing file as a
        // change and fires — Start() is what establishes "no change since we began watching".
        source.Start();
        Assert.False(source.PollOnce());
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
}
