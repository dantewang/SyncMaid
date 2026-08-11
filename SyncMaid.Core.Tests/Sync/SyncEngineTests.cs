using System.Text;
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

    // Nested orphan directories must come out whole. A non-recursive delete refuses a
    // directory that still holds a subdirectory, so the plan has to remove children before
    // parents; if that ordering reverses, the outer directory survives the run.
    [Fact]
    public async Task End_to_end_mirror_run_removes_nested_orphan_directories_deepest_first()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\stale\inner\deep.txt", "stale", t);

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        var remaining = fs.EnumerateDirectories(@"D:\dst").ToList();
        Assert.DoesNotContain("stale/inner", remaining);
        Assert.DoesNotContain("stale", remaining); // the parent, not just the child
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

    // Tree identity extends to directory metadata: a destination folder carries the
    // source folder's modified time, not the time the sync happened to create/touch it.
    [Fact]
    public async Task End_to_end_mirror_run_repairs_drifted_directory_times()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sourceTime = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\a\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\a\keep.txt", "keep", t);
        fs.SetDirectoryLastWriteTimeUtc(@"S:\src\a", sourceTime);

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.Mirror);
        var statuses = await new SyncEngine(fs).ExecuteAsync(Task(dest));

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.Equal(
            sourceTime,
            fs.ListTree(@"D:\dst").Directories.Single(d => d.RelativePath == "a").LastWriteTimeUtc);
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

    // Sync-core-design §8: "Content-verify toggle: off -> no read-back; on -> read-back
    // happens." The toggle is a per-destination opt-in that costs a full re-read, and it is
    // the only guard against silent same-length corruption (§5.3). Nothing joined the
    // Destination flag to SafeFileTransfer's verify parameter, so both
    // "Verify = destination.VerifyContents" initializers in SyncPlanner could be deleted
    // with the whole suite still green.
    [Fact]
    public async Task Verify_contents_on_rejects_a_silently_corrupted_copy()
    {
        var fs = new InMemoryFileSystem { CorruptWrites = true }; // same length, wrong bytes
        fs.AddFile(@"S:\src\a.txt", "the real bytes");
        fs.AddFile(@"D:\dst\a.txt", "PREVIOUS GOOD COPY");

        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.AddOnly)
        {
            VerifyContents = true,
        };

        var status = Assert.Single(await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(dest)));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Equal("PREVIOUS GOOD COPY", Encoding.UTF8.GetString(fs.ReadAllBytes(@"D:\dst\a.txt")));
    }

    // The other half of the same design item, and the tier boundary from
    // SafeFileTransferTests seen end-to-end: with the toggle off, a same-length corruption
    // is invisible to the length check and commits. Asserting this keeps the flag honest —
    // if the engine verified unconditionally, the opt-in would be meaningless.
    [Fact]
    public async Task Verify_contents_off_does_not_read_back_so_same_length_corruption_commits()
    {
        var fs = new InMemoryFileSystem { CorruptWrites = true };
        fs.AddFile(@"S:\src\a.txt", "abcdef");

        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.AddOnly)
        {
            VerifyContents = false,
        };

        var status = Assert.Single(await new SyncEngine(fs).ExecuteAsync(Task(dest)));

        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.NotEqual("abcdef", Encoding.UTF8.GetString(fs.ReadAllBytes(@"D:\dst\a.txt")));
        Assert.Equal(6, fs.GetStamp(@"D:\dst\a.txt").Length); // only the bytes differ
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
    public async Task Cancellation_before_the_run_starts_copies_nothing()
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

    // Cancelling *during* a run is the case the Stop button produces, and the one the
    // pre-cancelled token above cannot reach: ExecuteAsync is Task.Run(..., token), so an
    // already-cancelled token means the engine body never executes at all. Progress is
    // reported immediately before each operation is applied, so cancelling on the first
    // report lands the engine between operation 0 and operation 1.
    [Fact]
    public async Task Cancellation_mid_run_stops_between_operations_and_keeps_what_landed()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\b.txt", "b");
        fs.AddFile(@"S:\src\c.txt", "c");

        var dest = new Destination("d", @"D:\dst", new FilterRule[] { new AllFilesFilter() }, SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);
        using var cts = new CancellationTokenSource();
        var reports = 0;
        var progress = new CallbackProgress<SyncProgress>(_ =>
        {
            if (++reports == 1)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ExecuteAsync(Task(dest), cts.Token, progress));

        // The operation already in flight completes; the ones behind it never start. The
        // token is checked at the top of each iteration, so the second operation throws
        // before it is even reported — one report, one file.
        Assert.Equal(1, reports);
        var copied = Assert.Single(fs.EnumerateFiles(@"D:\dst")); // relative to the root

        // What landed is a complete file, not a half-written one, and no temp survives.
        Assert.DoesNotContain(".syncmaid-tmp-", copied);
        Assert.Equal(1, fs.GetStamp(@"D:\dst\" + copied).Length);
    }

    // Cancellation is not a destination failure: it propagates rather than being folded
    // into a status. The check between destinations is what makes that true even when the
    // queued destination has nothing to do — an already-in-sync destination plans zero
    // operations, so the per-operation check inside it never runs, and without the
    // between-destinations check the run would return a fabricated Success for it instead
    // of surfacing the cancellation.
    [Fact]
    public async Task Cancellation_mid_run_does_not_fabricate_a_status_for_an_idle_destination()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"D:\two\a.txt", "a"); // already in sync — plans zero operations

        var first = new Destination("first", @"D:\one", [new AllFilesFilter()], SyncStrategy.AddOnly);
        var second = new Destination("second", @"D:\two", [new AllFilesFilter()], SyncStrategy.AddOnly);
        var engine = new SyncEngine(fs);
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress<SyncProgress>(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ExecuteAsync(Task(first, second), cts.Token, progress));

        Assert.True(fs.FileExists(@"D:\one\a.txt")); // the in-flight copy completed
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class CollectingProgress : IProgress<SyncProgress>
    {
        private readonly List<SyncProgress> _reports;

        public CollectingProgress(List<SyncProgress> reports) => _reports = reports;

        public void Report(SyncProgress value) => _reports.Add(value);
    }
}
