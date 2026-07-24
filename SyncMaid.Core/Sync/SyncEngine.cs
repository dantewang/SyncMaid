using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using System.Runtime.ExceptionServices;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Runs a <see cref="SyncTask"/>: it enumerates the source once, then for each destination
/// applies its filters, plans the operations for its strategy, and applies them. Planning
/// and applying are delegated to
/// <see cref="SyncPlanner"/> and <see cref="SyncApplier"/> so this type only
/// orchestrates: enumerate → filter → plan → apply, with cancellation and progress.
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private readonly IFileSystem _fileSystem;
    private readonly IDestinationProviderFactory _destinations;
    private readonly RetryOptions _retry;

    /// <summary>Full constructor: an explicit destination-provider factory (the extension seam).</summary>
    public SyncEngine(IFileSystem fileSystem, IDestinationProviderFactory destinations, RetryOptions? retry = null)
    {
        _fileSystem = fileSystem;
        _destinations = destinations;
        _retry = retry ?? RetryOptions.Default;
    }

    /// <summary>Convenience: source and destinations are the same local filesystem (phase-1 default).</summary>
    public SyncEngine(IFileSystem fileSystem, RetryOptions? retry = null)
        : this(fileSystem, new LocalDestinationProviderFactory(fileSystem), retry)
    {
    }

    /// <summary>
    /// Executes <paramref name="task"/> against every destination, in order, returning
    /// each destination's outcome. A failure in one destination is captured as a failed
    /// status and does not stop the others. Honors <paramref name="cancellationToken"/>
    /// between operations and reports <see cref="SyncProgress"/> before each operation.
    /// </summary>
    /// <remarks>
    /// The work is synchronous filesystem I/O; we wrap it on a background thread via
    /// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> so callers
    /// (e.g. a UI) stay responsive and can await completion.
    /// </remarks>
    public Task<IReadOnlyList<DestinationSyncStatus>> ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null,
        IReadOnlySet<Guid>? confirmedMassDeletes = null)
    {
        return Task.Run(() => Execute(task, cancellationToken, progress, confirmedMassDeletes), cancellationToken);
    }

    /// <inheritdoc />
    public Task<MirrorDeletePreview> PreviewMirrorDeletionsAsync(
        SyncTask task,
        Guid destinationId,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                var destination = task.Destinations.FirstOrDefault(d => d.Id == destinationId);
                if (destination is null
                    || destination.Strategy != SyncStrategy.Mirror
                    || destination.Filters is not [AllFilesFilter] // invalid config never runs, so preview nothing
                    || RelativePaths.Overlaps(destination.LocalPath, task.SourcePath))
                {
                    return MirrorDeletePreview.None;
                }

                var provider = _destinations.Create(destination.Target);
                try
                {
                    var source = _fileSystem.ListTree(task.SourcePath);
                    var filtered = source.Files
                        .Where(file => destination.Includes(file.RelativePath))
                        .ToList();
                    var plan = SyncPlanner.Plan(
                        task.SourcePath, provider, destination, filtered, source.Directories);

                    var deletions = plan.Operations
                        .OfType<DeleteOperation>()
                        .Select(operation => operation.RelativePath)
                        .ToList();
                    return new MirrorDeletePreview(deletions.Count, deletions.Take(PreviewSampleSize).ToList());
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The preview is advisory: if the source vanished since the run that
                    // was blocked (e.g. the drive was unplugged), report nothing to
                    // confirm — the follow-up run surfaces the real failure.
                    return MirrorDeletePreview.None;
                }
            },
            cancellationToken);
    }

    private const int PreviewSampleSize = 25;

    /// <summary>
    /// How many operations may fail back-to-back before the destination is abandoned for
    /// this run. Scattered failures (one locked-out file here, one permission problem
    /// there) are isolated and the run continues; an unbroken run of them means the
    /// destination is unreachable, and every further attempt just burns retry backoff.
    /// </summary>
    private const int MaxConsecutiveFailures = 10;

    // The status line shows one sentence: name the first culprit, count the rest, and say
    // so when the run gave up early.
    private static string DescribeFailures(
        SyncOperationException first, int failureCount, bool abandoned)
    {
        var message = failureCount == 1
            ? first.Message
            : $"{first.Message} (and {failureCount - 1} more)";

        return abandoned
            ? $"{message}; stopped after {MaxConsecutiveFailures} consecutive failures."
            : message;
    }


    private IReadOnlyList<DestinationSyncStatus> Execute(
        SyncTask task,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress,
        IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        // Task shape convention (AGENT.md): Move is exclusive. Combinations have no
        // coherent semantics (destinations run in sequence, and Move empties the source
        // the others still treat as the truth), so the whole run is refused before any
        // file is touched. The editors prevent this; hand-edited config lands here.
        if (task.Destinations.Count > 1
            && task.Destinations.Any(destination => destination.Strategy == SyncStrategy.Move))
        {
            return task.Destinations
                .Select(destination => new DestinationSyncStatus(
                    destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                    "A Move destination must be the only destination of its task; no files were changed."))
                .ToList();
        }

        IReadOnlyList<ListedFile> sourceFiles = [];
        IReadOnlyList<ListedDirectory> sourceDirectories = [];
        ExceptionDispatchInfo? sourceEnumerationError = null;
        try
        {
            // One walk of the source serves every destination: files, stamps, and
            // directories together, with no per-file stat calls.
            var source = _fileSystem.ListTree(task.SourcePath);
            sourceFiles = source.Files;
            sourceDirectories = source.Directories;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            sourceEnumerationError = ExceptionDispatchInfo.Capture(exception);
        }

        var statuses = new List<DestinationSyncStatus>(task.Destinations.Count);
        foreach (var destination in task.Destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(ExecuteDestination(
                task,
                destination,
                sourceFiles,
                sourceDirectories,
                sourceEnumerationError,
                cancellationToken,
                progress,
                confirmedMassDeletes));
        }

        return statuses;
    }

    private DestinationSyncStatus ExecuteDestination(
        SyncTask task,
        Destination destination,
        IReadOnlyList<ListedFile> sourceFiles,
        IReadOnlyList<ListedDirectory> sourceDirectories,
        ExceptionDispatchInfo? sourceEnumerationError,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress,
        IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        try
        {
            // Task shape convention (AGENT.md): source and destinations never nest, in
            // either direction, for every strategy. A destination inside the source feeds
            // the app's own output back in as input; a source inside a destination makes
            // Mirror's orphan scan delete the live source. Reject the layout, don't
            // engineer around it.
            if (RelativePaths.Overlaps(destination.LocalPath, task.SourcePath))
            {
                return new DestinationSyncStatus(
                    destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                    "Destination must be a separate folder outside the source (and not contain it); no files were changed.");
            }

            // Product rule: Mirror's contract is tree identity — the destination
            // replicates the whole source tree — so file filters have no coherent
            // meaning for it. The editor hides the filter section for Mirror;
            // hand-edited config is refused here, before any file is touched.
            if (destination.Strategy == SyncStrategy.Mirror
                && destination.Filters is not [AllFilesFilter])
            {
                return new DestinationSyncStatus(
                    destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                    "Mirror replicates the whole source tree and cannot be combined with file filters; no files were changed.");
            }

            if (sourceEnumerationError is not null)
            {
                sourceEnumerationError.Throw();
            }

            var filtered = sourceFiles
                .Where(file => destination.Includes(file.RelativePath))
                .ToList();

            var provider = _destinations.Create(destination.Target);

            var plan = SyncPlanner.Plan(
                task.SourcePath, provider, destination, filtered, sourceDirectories);

            // Guard Mirror deletions before applying anything: an empty/unavailable source is
            // refused; a mass-delete needs the user's confirmation (unless already given).
            var deleteCount = plan.Operations.Count(operation => operation is DeleteOperation);
            if (deleteCount > 0)
            {
                var verdict = MirrorGuard.Evaluate(
                    deleteCount,
                    plan.DestinationFileCount,
                    sourceIsEmpty: filtered.Count == 0,
                    destination.MassDeleteThreshold,
                    overrideMassDelete: confirmedMassDeletes?.Contains(destination.Id) ?? false);

                if (verdict == MirrorGuardVerdict.EmptySource)
                {
                    return new DestinationSyncStatus(
                        destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                        "Source is empty or unavailable; skipped deletions to avoid wiping the destination.");
                }

                if (verdict == MirrorGuardVerdict.NeedsConfirmation)
                {
                    return new DestinationSyncStatus(
                        destination.Id, SyncOutcome.NeedsConfirmation, DateTimeOffset.UtcNow, 0,
                        $"Would delete {deleteCount} files no longer in the source — review before syncing.");
                }
            }

            var copied = new List<string>();
            var deferred = new List<string>();
            SyncOperationException? firstFailure = null;
            var failureCount = 0;
            var consecutiveFailures = 0;
            var abandoned = false;

            for (var i = 0; i < plan.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = plan.Operations[i];
                progress?.Report(new SyncProgress(destination, operation, i, plan.Operations.Count));

                // Retry genuinely transient I/O (an antivirus scan, a momentary sharing
                // violation) before judging the operation. Annotate any surviving failure
                // with the file/operation so the status names the culprit.
                try
                {
                    TransientRetry.Execute(
                        () => SyncApplier.Apply(_fileSystem, provider, operation),
                        _retry.MaxAttempts,
                        attempt =>
                        {
                            if (_retry.BaseDelay > TimeSpan.Zero)
                            {
                                Thread.Sleep(_retry.BaseDelay * attempt);
                            }
                        });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A file still being written, or one another process is holding open,
                    // is not a failure — and nothing was written to the destination, so
                    // the tree is left consistent. Defer it; the next run picks it up once
                    // the writer is done.
                    if (exception is SourceBusyException || FileBusy.IsBusy(exception))
                    {
                        deferred.Add(operation.RelativePath);
                        consecutiveFailures = 0;
                        continue;
                    }

                    // One bad file must not cost every operation queued behind it, so the
                    // run carries on — but a wall of consecutive failures means the
                    // destination itself is gone (unplugged drive, dropped share), and
                    // grinding through thousands of doomed operations helps nobody.
                    failureCount++;
                    firstFailure ??= new SyncOperationException(operation, exception);
                    if (++consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        abandoned = true;
                        break;
                    }

                    continue;
                }

                consecutiveFailures = 0;
                if (operation is CopyOperation or MoveOperation)
                {
                    copied.Add(operation.RelativePath);
                }
            }

            // Severity ladder: a real failure outranks a merely deferred file, which in
            // turn outranks a clean run. FilesCopied stays honest in every case — the
            // files that did make it across are reported even when something else failed.
            var outcome = firstFailure is not null
                ? SyncOutcome.Failed
                : deferred.Count > 0
                    ? SyncOutcome.Incomplete
                    : SyncOutcome.Success;

            return new DestinationSyncStatus(
                destination.Id,
                outcome,
                DateTimeOffset.UtcNow,
                copied.Count,
                firstFailure is null ? null : DescribeFailures(firstFailure, failureCount, abandoned))
            {
                CopiedRelativePaths = copied,
                FilesDeferred = deferred.Count,
                DeferredRelativePaths = deferred,
            };
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a destination failure; let it propagate.
        }
        catch (Exception exception)
        {
            return new DestinationSyncStatus(
                destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, FilesCopied: 0, exception.Message);
        }
    }
}
