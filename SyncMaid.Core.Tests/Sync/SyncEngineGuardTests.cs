using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// Engine-level coverage of the Mirror delete guardrail and Recycle Bin deletes — the
/// "don't wipe the backup" and "deletions are recoverable" safety properties.
/// </summary>
public class SyncEngineGuardTests
{
    private static SyncTask Mirror(InMemoryFileSystem fs, Destination dest) =>
        new("t", @"S:\src", new ManualTrigger(), [dest]);

    [Fact]
    public async Task Empty_or_unavailable_source_does_not_wipe_the_mirror()
    {
        var fs = new InMemoryFileSystem();
        // No files under the source root (it's missing/unavailable).
        fs.AddFile(@"D:\dst\important1.txt", "a");
        fs.AddFile(@"D:\dst\important2.txt", "b");
        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror);

        var statuses = await new SyncEngine(fs).ExecuteAsync(Mirror(fs, dest));

        Assert.Equal(SyncOutcome.Failed, Assert.Single(statuses).Outcome);
        Assert.True(fs.FileExists(@"D:\dst\important1.txt")); // nothing deleted
        Assert.True(fs.FileExists(@"D:\dst\important2.txt"));
    }

    [Fact]
    public async Task Mass_delete_over_threshold_aborts_without_deleting()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\keep.txt", "k");
        fs.AddFile(@"D:\dst\keep.txt", "k");
        for (var i = 0; i < 19; i++)
        {
            fs.AddFile($@"D:\dst\orphan{i}.txt", "o"); // 19 of 20 files would be deleted
        }

        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror);

        var statuses = await new SyncEngine(fs).ExecuteAsync(Mirror(fs, dest));

        Assert.Equal(SyncOutcome.Failed, Assert.Single(statuses).Outcome);
        Assert.Equal(20, fs.EnumerateFiles(@"D:\dst").Count()); // all still present
    }

    [Fact]
    public async Task Mirror_sends_orphans_to_the_recycle_bin_by_default()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\keep.txt", "k");
        fs.AddFile(@"D:\dst\keep.txt", "k");
        fs.AddFile(@"D:\dst\orphan.txt", "o");
        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror);

        await new SyncEngine(fs).ExecuteAsync(Mirror(fs, dest));

        Assert.False(fs.FileExists(@"D:\dst\orphan.txt"));
        Assert.Contains(fs.Recycled, p => p.Contains("orphan.txt"));
    }

    [Fact]
    public async Task Permanent_delete_mode_does_not_use_the_recycle_bin()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\keep.txt", "k");
        fs.AddFile(@"D:\dst\keep.txt", "k");
        fs.AddFile(@"D:\dst\orphan.txt", "o");
        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            DeleteMode = DeleteMode.Permanent,
        };

        await new SyncEngine(fs).ExecuteAsync(Mirror(fs, dest));

        Assert.False(fs.FileExists(@"D:\dst\orphan.txt"));
        Assert.DoesNotContain(fs.Recycled, p => p.Contains("orphan.txt"));
    }
}
