using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

public class SyncEngineTests
{
    private const string SourceRoot = @"S:\src";

    private static SyncTask Task(params Destination[] destinations) =>
        new("task", SourceRoot, new ManualTrigger(), destinations);

    [Fact]
    public async Task End_to_end_mirror_run_copies_and_deletes()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\a.txt", "a", t);
        fs.AddFile(@"S:\src\sub\b.txt", "b", t);
        fs.AddFile(@"D:\dst\orphan.txt", "stale", t); // should be removed by Mirror

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(dest));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.True(fs.FileExists(@"D:\dst\sub\b.txt"));
        Assert.False(fs.FileExists(@"D:\dst\orphan.txt"));
    }

    // The Eagle shape: removing an item drops a whole per-item folder from the source;
    // Mirror must remove the destination folder, not just the files in it.
    [Fact]
    public async Task End_to_end_mirror_run_removes_directories_no_longer_in_the_source()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\image1\photo.png", "stale", t);
        fs.AddFile(@"D:\dst\image1\thumb.png", "stale", t);
        fs.AddFile(@"D:\dst\image1\metadata.json", "stale", t);

        // 3 of the 4 destination files are orphans, but the ratio guard only arms on
        // destinations holding at least MirrorGuard.MinDestinationFilesForRatioGuard files.
        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.DoesNotContain(fs.AllPaths, path => path.StartsWith(@"D:/dst/image1"));
        Assert.Contains(@"D:/dst/image1", fs.DeletedDirectories);
    }

    // The tree-identity contract: a tree compare of source and destination must report
    // identical after a run — empty directories included, in both directions.
    [Fact]
    public async Task End_to_end_mirror_run_replicates_an_empty_source_directory()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.EnsureDirectory(@"S:\src\empty");

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.Contains("empty", fs.EnumerateDirectories(@"D:\dst"));
    }

    [Fact]
    public async Task End_to_end_mirror_run_keeps_a_directory_the_source_still_has_after_deleting_its_files()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.EnsureDirectory(@"S:\src\a");                 // emptied of files, but still exists
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\a\orphan.txt", "stale", t);

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.False(fs.FileExists(@"D:\dst\a\orphan.txt"));
        Assert.Empty(fs.DeletedDirectories); // the folder itself must survive
    }

    // A destination that does not exist yet is an empty destination — the first run
    // creates it. Only a missing SOURCE is an error (see the guard tests).
    [Fact]
    public async Task First_mirror_run_into_a_missing_destination_creates_it()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a"); // D:\dst does not exist at all

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, statuses[0].Outcome);
        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public async Task Engine_applies_filters_so_only_matching_files_sync()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\photo.jpg", "img");
        fs.AddFile(@"S:\src\notes.txt", "txt");

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new ExtensionFilter("jpg") }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(dest));

        Assert.True(fs.FileExists(@"D:\dst\photo.jpg"));
        Assert.False(fs.FileExists(@"D:\dst\notes.txt"));
    }

    [Fact]
    public async Task Filters_are_evaluated_in_order_first_match_wins_inclusion()
    {
        // docs/readme.md matches the PathFilter; song.mp3 matches neither rule.
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\docs\readme.md", "doc");
        fs.AddFile(@"S:\src\docs\image.png", "img");
        fs.AddFile(@"S:\src\music\song.mp3", "song");

        var dest = new Destination(
            "d", @"D:\dst",
            new FilterRule[] { new PathFilter("docs"), new ExtensionFilter("mp3") },
            SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(dest));

        Assert.True(fs.FileExists(@"D:\dst\docs\readme.md"));
        Assert.True(fs.FileExists(@"D:\dst\docs\image.png")); // under docs/ via PathFilter
        Assert.True(fs.FileExists(@"D:\dst\music\song.mp3"));  // via ExtensionFilter
    }

    [Fact]
    public async Task Engine_runs_multiple_destinations()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");

        var d1 = new Destination("d1", @"D:\one", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var d2 = new Destination("d2", @"E:\two", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(d1, d2));

        Assert.True(fs.FileExists(@"D:\one\a.txt"));
        Assert.True(fs.FileExists(@"E:\two\a.txt"));
    }

    [Fact]
    public async Task Move_strategy_removes_files_from_source()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\b.txt", "b");

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Move);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(dest));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.True(fs.FileExists(@"D:\dst\b.txt"));
        Assert.False(fs.FileExists(@"S:\src\a.txt"));
        Assert.False(fs.FileExists(@"S:\src\b.txt"));
    }

    [Fact]
    public async Task Move_stamp_mismatch_returns_failed_status_and_keeps_source()
    {
        var fs = new InMemoryFileSystem { SetLastWriteTimeOffset = TimeSpan.FromSeconds(1) };
        fs.AddFile(@"S:\src\a.txt", "precious");
        var destination = new Destination(
            "d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.Move);

        var status = Assert.Single(await new SyncEngine(fs).ExecuteAsync(Task(destination)));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("Refusing to delete source", status.Error);
        Assert.True(fs.FileExists(@"S:\src\a.txt"));
    }

    [Fact]
    public async Task Progress_is_reported_for_each_operation()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\b.txt", "b");

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);
        var reports = new List<SyncProgress>();
        // A synchronous IProgress<T> so reports are observed deterministically; the
        // built-in Progress<T> marshals callbacks async, which races in tests.
        var progress = new CollectingProgress(reports);

        await engine.ExecuteAsync(Task(dest), progress: progress);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.Equal(2, r.TotalOperations));
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        var fs = new InMemoryFileSystem();
        for (var i = 0; i < 5; i++)
        {
            fs.AddFile($@"S:\src\file{i}.txt", $"content{i}");
        }

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled before the run starts

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ExecuteAsync(Task(dest), cts.Token));

        Assert.DoesNotContain(fs.AllPaths, path => path.StartsWith(@"D:/dst")); // nothing copied
    }

    private sealed class CollectingProgress : IProgress<SyncProgress>
    {
        private readonly List<SyncProgress> _reports;

        public CollectingProgress(List<SyncProgress> reports) => _reports = reports;

        public void Report(SyncProgress value) => _reports.Add(value);
    }
}
