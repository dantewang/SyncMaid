using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// What a Move destination does to the source and to the folder it fills: the folders a run
/// empties are cleaned up, and a destination that flattens has to decide what happens when
/// two files want the same name.
/// </summary>
public class MoveDestinationTests
{
    private static SyncTask Task(Destination destination) =>
        new("file downloads", @"S:\downloads", new ManualTrigger(), [destination]);

    private static Destination Move(string path, params FilterRule[] filters) =>
        new("archive", path, filters.Length == 0 ? [new AllFilesFilter()] : filters, SyncStrategy.Move);

    private static string TextAt(InMemoryFileSystem fs, string path) =>
        System.Text.Encoding.UTF8.GetString(fs.ReadAllBytes(path));

    [Fact]
    public async Task Source_folders_the_run_emptied_are_removed()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\2026\january\report.pdf", "r");

        var status = Assert.Single(await new SyncEngine(fs, RetryOptions.None)
            .ExecuteAsync(Task(Move(@"D:\archive"))));

        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.True(fs.FileExists(@"D:\archive\2026\january\report.pdf"));

        // Both levels go: the child is emptied by the move, and the parent by the child.
        Assert.Contains(@"S:/downloads/2026/january", fs.DeletedDirectories);
        Assert.Contains(@"S:/downloads/2026", fs.DeletedDirectories);

        // Never the source root — a watched folder that deletes itself stops working.
        Assert.DoesNotContain(@"S:/downloads", fs.DeletedDirectories);
    }

    [Fact]
    public async Task A_source_folder_that_still_holds_something_is_kept()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\mixed\report.pdf", "r");
        fs.AddFile(@"S:\downloads\mixed\notes.txt", "n");

        await new SyncEngine(fs, RetryOptions.None)
            .ExecuteAsync(Task(Move(@"D:\archive", new ExtensionFilter("pdf"))));

        Assert.True(fs.FileExists(@"S:\downloads\mixed\notes.txt"));
        Assert.Empty(fs.DeletedDirectories);
    }

    // A folder that was already empty holds no moved file, so it is not an ancestor of one
    // and the cleanup never considers it: emptying the inbox is the run's business, tidying
    // folders the user made is not.
    [Fact]
    public async Task A_folder_that_was_already_empty_before_the_run_is_left_alone()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\report.pdf", "r");
        fs.EnsureDirectory(@"S:\downloads\waiting-room");

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Move(@"D:\archive")));

        Assert.Empty(fs.DeletedDirectories);
    }

    [Fact]
    public async Task Emptied_source_folders_follow_the_destination_delete_mode()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\january\report.pdf", "r");
        var permanent = Move(@"D:\archive") with { DeleteMode = DeleteMode.Permanent };

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(permanent));

        Assert.Contains(@"S:/downloads/january", fs.DeletedDirectories);
        Assert.Empty(fs.RecycledDirectories);
    }

    [Fact]
    public async Task Emptied_source_folders_go_to_the_recycle_bin_by_default()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\january\report.pdf", "r");

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Move(@"D:\archive")));

        Assert.Contains(@"S:/downloads/january", fs.RecycledDirectories);
    }

    [Fact]
    public async Task Flattening_drops_the_source_folders()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\2026\january\report.pdf", "r");
        var flattening = Move(@"D:\archive") with { FlattenStructure = true };

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(flattening));

        Assert.True(fs.FileExists(@"D:\archive\report.pdf"));
        Assert.False(fs.FileExists(@"D:\archive\2026\january\report.pdf"));
    }

    // Skip is the conservative default: nothing at the destination is touched, nothing is
    // renamed behind the user's back, and the file stays where they can still find it.
    [Fact]
    public async Task A_flattened_name_that_is_taken_is_skipped_and_reported()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\january\report.pdf", "new");
        fs.AddFile(@"D:\archive\report.pdf", "existing");
        var flattening = Move(@"D:\archive") with { FlattenStructure = true };

        var status = Assert.Single(await new SyncEngine(fs, RetryOptions.None)
            .ExecuteAsync(Task(flattening)));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("already there", status.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing", TextAt(fs, @"D:\archive\report.pdf"));
        Assert.True(fs.FileExists(@"S:\downloads\january\report.pdf"));
    }

    [Fact]
    public async Task The_suffix_policy_numbers_a_taken_name_instead_of_skipping_it()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\january\report.pdf", "new");
        fs.AddFile(@"D:\archive\report.pdf", "existing");
        var flattening = Move(@"D:\archive") with
        {
            FlattenStructure = true,
            CollisionPolicy = FileNameCollisionPolicy.Suffix,
        };

        var status = Assert.Single(await new SyncEngine(fs, RetryOptions.None)
            .ExecuteAsync(Task(flattening)));

        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.Equal("existing", TextAt(fs, @"D:\archive\report.pdf"));
        Assert.Equal("new", TextAt(fs, @"D:\archive\report (2).pdf"));
    }

    // Two source files can only want the same name once flattening removed the folders that
    // kept them apart, so the collision is between files of the same run — not with the
    // destination, which has neither of them yet.
    [Fact]
    public async Task Two_source_files_flattened_onto_one_name_collide_with_each_other()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\january\report.pdf", "january");
        fs.AddFile(@"S:\downloads\february\report.pdf", "february");
        var flattening = Move(@"D:\archive") with
        {
            FlattenStructure = true,
            CollisionPolicy = FileNameCollisionPolicy.Suffix,
        };

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(flattening));

        Assert.True(fs.FileExists(@"D:\archive\report.pdf"));
        Assert.True(fs.FileExists(@"D:\archive\report (2).pdf"));
    }
}
