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

    // A destination nested inside the source must not treat its own output as source
    // content — with live triggers, re-copying it one level deeper each run would loop
    // until the disk fills.
    [Fact]
    public async Task Nested_destination_does_not_recopy_its_own_output()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        var dest = new Destination(
            "backup", @"S:\src\backup", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);

        await engine.ExecuteAsync(Task(dest));
        Assert.True(fs.FileExists(@"S:\src\backup\a.txt"));

        var statuses = await engine.ExecuteAsync(Task(dest)); // source now contains backup\a.txt
        Assert.Equal(SyncOutcome.Success, statuses[0].Outcome);
        Assert.Equal(0, statuses[0].FilesCopied);
        Assert.False(fs.FileExists(@"S:\src\backup\backup\a.txt"));
    }

    [Fact]
    public async Task Nested_mirror_destination_keeps_its_own_content_out_of_planning()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        var dest = new Destination(
            "backup", @"S:\src\backup", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var engine = new SyncEngine(fs);

        var first = await engine.ExecuteAsync(Task(dest));
        Assert.Equal(SyncOutcome.Success, first[0].Outcome);
        Assert.True(fs.FileExists(@"S:\src\backup\a.txt"));

        var second = await engine.ExecuteAsync(Task(dest));
        Assert.Equal(SyncOutcome.Success, second[0].Outcome);
        Assert.Equal(0, second[0].FilesCopied);
        Assert.True(fs.FileExists(@"S:\src\backup\a.txt"));  // not planned as its own deletion
        Assert.False(fs.FileExists(@"S:\src\backup\backup\a.txt"));
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

        Assert.Empty(fs.EnumerateFiles(@"D:\dst")); // nothing copied
    }

    private sealed class CollectingProgress : IProgress<SyncProgress>
    {
        private readonly List<SyncProgress> _reports;

        public CollectingProgress(List<SyncProgress> reports) => _reports = reports;

        public void Report(SyncProgress value) => _reports.Add(value);
    }
}
