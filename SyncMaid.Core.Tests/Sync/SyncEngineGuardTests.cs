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

    // Task shape convention: source and destinations never nest, in either direction,
    // for every strategy. The Mirror source-inside-destination case is the data-loss
    // trap this closes: the orphan scan of S:\ would otherwise delete the live source.
    [Theory]
    [InlineData(SyncStrategy.AddOnly, @"S:\src")]
    [InlineData(SyncStrategy.AddOnly, @"S:\src\nested")]
    [InlineData(SyncStrategy.AddOnly, @"S:\")]
    [InlineData(SyncStrategy.Mirror, @"S:\src")]
    [InlineData(SyncStrategy.Mirror, @"S:\src\nested")]
    [InlineData(SyncStrategy.Mirror, @"S:\")]
    [InlineData(SyncStrategy.Move, @"S:\src")]
    [InlineData(SyncStrategy.Move, @"S:\src\nested")]
    [InlineData(SyncStrategy.Move, @"S:\")]
    public async Task Nested_destination_fails_without_touching_any_file(
        SyncStrategy strategy, string destinationPath)
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\important.txt", "keep me");
        var pathsBefore = fs.AllPaths;
        var destination = new Destination(
            "unsafe", destinationPath, [new AllFilesFilter()], strategy);
        var task = new SyncTask("nested", @"S:\src", new ManualTrigger(), [destination]);

        var status = Assert.Single(await new SyncEngine(fs).ExecuteAsync(task));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("outside the source", status.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(pathsBefore, fs.AllPaths); // zero filesystem mutations
    }

    // Task shape convention: Move is exclusive. Destinations run in sequence and Move
    // empties the source the others still treat as the truth, so any combination is
    // refused whole — every destination fails, nothing is touched.
    [Theory]
    [InlineData(SyncStrategy.AddOnly)]
    [InlineData(SyncStrategy.Mirror)]
    [InlineData(SyncStrategy.Move)]
    public async Task Move_combined_with_any_other_destination_fails_the_whole_run(
        SyncStrategy otherStrategy)
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\important.txt", "keep me");
        var pathsBefore = fs.AllPaths;
        var move = new Destination("move", @"D:\archive", [new AllFilesFilter()], SyncStrategy.Move);
        var other = new Destination("other", @"E:\backup", [new AllFilesFilter()], otherStrategy);
        var task = new SyncTask("combo", @"S:\src", new ManualTrigger(), [other, move]);

        var statuses = await new SyncEngine(fs).ExecuteAsync(task);

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, status =>
        {
            Assert.Equal(SyncOutcome.Failed, status.Outcome);
            Assert.Contains("only destination", status.Error, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(pathsBefore, fs.AllPaths); // zero filesystem mutations
    }

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
    public async Task Empty_source_and_empty_mirror_destination_succeeds_as_a_no_op()
    {
        var fs = new InMemoryFileSystem();
        var destination = new Destination(
            "empty mirror", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror);

        var status = Assert.Single(await new SyncEngine(fs).ExecuteAsync(Mirror(fs, destination)));

        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.Equal(0, status.FilesCopied);
        Assert.Null(status.Error);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(20, 0.5)]
    public async Task Mirror_with_no_filtered_source_files_fails_without_deletions(
        int destinationFileCount,
        double massDeleteThreshold)
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\photo.jpg", "source exists");
        for (var i = 0; i < destinationFileCount; i++)
        {
            fs.AddFile($@"D:\dst\important{i}.txt", "keep");
        }

        var destination = new Destination(
            "filtered mirror", @"D:\dst", [new ExtensionFilter("pdf")], SyncStrategy.Mirror)
        {
            MassDeleteThreshold = massDeleteThreshold,
        };

        var status = Assert.Single(await new SyncEngine(fs).ExecuteAsync(Mirror(fs, destination)));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("filters matched no files", status.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(destinationFileCount, fs.EnumerateFiles(@"D:\dst").Count());
    }

    private static InMemoryFileSystem MassDeleteScenario(out Destination dest)
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\keep.txt", "k");
        fs.AddFile(@"D:\dst\keep.txt", "k");
        for (var i = 0; i < 19; i++)
        {
            fs.AddFile($@"D:\dst\orphan{i}.txt", "o"); // 19 of 20 files would be deleted
        }

        dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Mirror);
        return fs;
    }

    [Fact]
    public async Task Mass_delete_over_threshold_needs_confirmation_and_deletes_nothing()
    {
        var fs = MassDeleteScenario(out var dest);

        var statuses = await new SyncEngine(fs).ExecuteAsync(Mirror(fs, dest));

        Assert.Equal(SyncOutcome.NeedsConfirmation, Assert.Single(statuses).Outcome);
        Assert.Equal(20, fs.EnumerateFiles(@"D:\dst").Count()); // all still present
    }

    [Fact]
    public async Task A_confirmed_mass_delete_proceeds()
    {
        var fs = MassDeleteScenario(out var dest);

        var statuses = await new SyncEngine(fs).ExecuteAsync(
            Mirror(fs, dest), confirmedMassDeletes: new HashSet<Guid> { dest.Id });

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.Single(fs.EnumerateFiles(@"D:\dst")); // only keep.txt remains
    }

    [Fact]
    public async Task Preview_lists_the_files_a_mass_delete_would_remove()
    {
        var fs = MassDeleteScenario(out var dest);

        var preview = await new SyncEngine(fs).PreviewMirrorDeletionsAsync(Mirror(fs, dest), dest.Id);

        Assert.Equal(19, preview.Count);
        Assert.All(preview.Sample, path => Assert.Contains("orphan", path));
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
