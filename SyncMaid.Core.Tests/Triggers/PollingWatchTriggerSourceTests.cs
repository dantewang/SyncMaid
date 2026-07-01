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
}
