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
