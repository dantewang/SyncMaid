using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// A file that is still being written, or held open by another process, must not fail the
/// destination or cost the operations queued behind it: the engine defers it, applies
/// everything else, and reports <see cref="SyncOutcome.Incomplete"/> so the next run
/// picks it up. Genuine failures stay failures, and stop being contagious.
/// </summary>
public class SyncEngineBusyFileTests
{
    private const string SourceRoot = @"S:\src";
    private const string DestRoot = @"D:\dst";

    private static SyncTask Task(Destination destination) =>
        new("t", SourceRoot, new ManualTrigger(), [destination]);

    private static Destination Dest(SyncStrategy strategy = SyncStrategy.AddOnly) =>
        new("d", DestRoot, [new AllFilesFilter()], strategy);

    // A source that keeps changing while the run is in flight: by the time its copy is
    // applied, its stamp no longer matches the one the planner saw.
    [Fact]
    public async Task A_file_still_being_written_is_deferred_and_everything_else_still_syncs()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{SourceRoot}\busy.dat", "the first bytes of a long save");
        fs.AddFile($@"{SourceRoot}\calm.txt", "settled");
        var progress = new CallbackProgress(report =>
        {
            if (report.Operation.RelativePath == "busy.dat")
            {
                fs.AddFile($@"{SourceRoot}\busy.dat", "the first bytes of a long save, plus more");
            }
        });

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Dest()), progress: progress));

        Assert.Equal(SyncOutcome.Incomplete, status.Outcome);
        Assert.Null(status.Error); // in use is not an error
        Assert.Equal(["busy.dat"], status.DeferredRelativePaths);
        Assert.Equal(1, status.FilesDeferred);
        Assert.Equal(["calm.txt"], status.CopiedRelativePaths);
        // A deferred file leaves the destination untouched — no half-written copy.
        Assert.False(fs.FileExists($@"{DestRoot}\busy.dat"));
        Assert.True(fs.FileExists($@"{DestRoot}\calm.txt"));
    }

    [Fact]
    public async Task A_locked_source_file_is_deferred_after_its_retries_are_exhausted()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{SourceRoot}\locked.dat", "held open by another process");
        fs.AddFile($@"{SourceRoot}\calm.txt", "settled");
        fs.LockPath($@"{SourceRoot}\locked.dat");

        var status = Assert.Single(await new SyncEngine(
            fs, new RetryOptions(MaxAttempts: 3, BaseDelay: TimeSpan.Zero)).ExecuteAsync(Task(Dest())));

        Assert.Equal(SyncOutcome.Incomplete, status.Outcome);
        Assert.Equal(["locked.dat"], status.DeferredRelativePaths);
        Assert.False(fs.FileExists($@"{DestRoot}\locked.dat"));
        Assert.True(fs.FileExists($@"{DestRoot}\calm.txt"));
    }

    // Mirror's deletions run against the destination, which can be held open just as the
    // source can — a locked destination file is deferred, not failed.
    [Fact]
    public async Task A_locked_destination_delete_is_deferred()
    {
        var fs = new InMemoryFileSystem
        {
            FailDeletePathFragment = "orphan",
            DeleteFailure = () => InMemoryFileSystem.SharingViolation("orphan.txt"),
        };
        fs.AddFile($@"{SourceRoot}\keep.txt", "keep");
        fs.AddFile($@"{DestRoot}\keep.txt", "keep");
        fs.AddFile($@"{DestRoot}\orphan.txt", "held open");
        var destination = Dest(SyncStrategy.Mirror) with { DeleteMode = DeleteMode.Permanent };

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(destination)));

        Assert.Equal(SyncOutcome.Incomplete, status.Outcome);
        Assert.Equal(["orphan.txt"], status.DeferredRelativePaths);
        Assert.True(fs.FileExists($@"{DestRoot}\orphan.txt")); // still there, to be retried
    }

    [Fact]
    public async Task A_failed_file_no_longer_costs_the_operations_queued_behind_it()
    {
        // "nope" has non-hex letters, so the fragment cannot accidentally match the
        // random hex suffix of another file's temp copy.
        var fs = new InMemoryFileSystem { FailWritePathFragment = "nope" };
        fs.AddFile($@"{SourceRoot}\nope.txt", "cannot be written");
        fs.AddFile($@"{SourceRoot}\after.txt", "queued behind the failure");

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Dest())));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("nope.txt", status.Error);
        Assert.True(fs.FileExists($@"{DestRoot}\after.txt")); // the run carried on
        Assert.Equal(["after.txt"], status.CopiedRelativePaths);
        Assert.Equal(1, status.FilesCopied); // and reports what actually made it across
    }

    // A destination that has gone away fails every operation; the run must give up rather
    // than grind through the whole plan burning retry backoff on each one.
    [Fact]
    public async Task A_destination_failing_every_operation_is_abandoned_early()
    {
        var fs = new InMemoryFileSystem { FailWritePathFragment = "dst" };
        for (var i = 0; i < 40; i++)
        {
            fs.AddFile($@"{SourceRoot}\file{i}.txt", $"content{i}");
        }

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Dest())));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Contains("consecutive failures", status.Error);
        Assert.Contains("and 9 more", status.Error); // stopped at 10 of 40, not all 40
        Assert.Equal(0, status.FilesCopied);
    }

    // Severity ladder: a real failure outranks a merely deferred file.
    [Fact]
    public async Task A_run_with_both_a_failure_and_a_deferral_reports_failed()
    {
        var fs = new InMemoryFileSystem { FailWritePathFragment = "nope" };
        fs.AddFile($@"{SourceRoot}\locked.dat", "held open");
        fs.AddFile($@"{SourceRoot}\nope.txt", "cannot be written");
        fs.LockPath($@"{SourceRoot}\locked.dat");

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Dest())));

        Assert.Equal(SyncOutcome.Failed, status.Outcome);
        Assert.Equal(1, status.FilesDeferred); // still reported alongside the failure
    }

    [Fact]
    public async Task A_clean_run_still_reports_success()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($@"{SourceRoot}\a.txt", "a");

        var status = Assert.Single(
            await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(Task(Dest())));

        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.Equal(0, status.FilesDeferred);
        Assert.Empty(status.DeferredRelativePaths);
    }

    private sealed class CallbackProgress(Action<SyncProgress> callback) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => callback(value);
    }
}
