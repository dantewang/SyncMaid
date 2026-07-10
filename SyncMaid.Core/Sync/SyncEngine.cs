using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

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
                if (destination is null || destination.Strategy != SyncStrategy.Mirror)
                {
                    return MirrorDeletePreview.None;
                }

                var provider = _destinations.Create(destination.Target);
                var sourceFiles = _fileSystem.EnumerateFiles(task.SourcePath).ToList();
                var filtered = WithoutNestedDestinationFiles(sourceFiles, task.SourcePath, destination)
                    .Where(destination.Includes)
                    .ToList();
                var plan = SyncPlanner.Plan(_fileSystem, task.SourcePath, provider, destination, filtered);

                var deletions = plan.Operations
                    .OfType<DeleteOperation>()
                    .Select(operation => operation.RelativePath)
                    .ToList();
                return new MirrorDeletePreview(deletions.Count, deletions.Take(PreviewSampleSize).ToList());
            },
            cancellationToken);
    }

    private const int PreviewSampleSize = 25;

    // A destination rooted inside the source would otherwise see its own output as source
    // content: every run would copy the previous run's files one level deeper
    // (backup/backup/…), and with trigger runs coalescing instead of being suppressed, that
    // feedback loop never quiesces. A destination's own subtree is not source content, for
    // any strategy. (Move refuses nested destinations outright before this applies.)
    private static IReadOnlyList<string> WithoutNestedDestinationFiles(
        IReadOnlyList<string> sourceFiles,
        string sourcePath,
        Destination destination)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalPath))
        {
            return sourceFiles;
        }

        var nestedPrefix = RelativePaths.TryGetPrefixWithin(sourcePath, destination.LocalPath);
        if (nestedPrefix is null)
        {
            return sourceFiles;
        }

        return sourceFiles
            .Where(file => !file.StartsWith(nestedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private IReadOnlyList<DestinationSyncStatus> Execute(
        SyncTask task,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress,
        IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        IReadOnlyList<string> sourceFiles = [];
        Exception? sourceEnumerationError = null;
        try
        {
            sourceFiles = _fileSystem.EnumerateFiles(task.SourcePath).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            sourceEnumerationError = exception;
        }

        var statuses = new List<DestinationSyncStatus>(task.Destinations.Count);
        foreach (var destination in task.Destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(ExecuteDestination(
                task,
                destination,
                sourceFiles,
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
        IReadOnlyList<string> sourceFiles,
        Exception? sourceEnumerationError,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress,
        IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        try
        {
            if (destination.Strategy == SyncStrategy.Move
                && !string.IsNullOrWhiteSpace(destination.LocalPath)
                && (RelativePaths.AreEquivalent(destination.LocalPath, task.SourcePath)
                    || RelativePaths.IsDescendantOf(destination.LocalPath, task.SourcePath)))
            {
                return new DestinationSyncStatus(
                    destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                    "Move destination must be different from and outside the source folder; no files were changed.");
            }

            if (sourceEnumerationError is not null)
            {
                throw sourceEnumerationError;
            }

            var visibleSource = WithoutNestedDestinationFiles(sourceFiles, task.SourcePath, destination);
            var filtered = visibleSource.Where(destination.Includes).ToList();
            if (destination.Strategy == SyncStrategy.Mirror
                && filtered.Count == 0
                && visibleSource.Count > 0)
            {
                return new DestinationSyncStatus(
                    destination.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                    "Filters matched no files; skipped deletions to avoid wiping the destination.");
            }

            var provider = _destinations.Create(destination.Target);

            var plan = SyncPlanner.Plan(_fileSystem, task.SourcePath, provider, destination, filtered);

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

            var filesCopied = 0;
            for (var i = 0; i < plan.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = plan.Operations[i];
                progress?.Report(new SyncProgress(destination, operation, i, plan.Operations.Count));

                // Retry transient I/O (a locked file, a brief sharing violation) before
                // failing the whole destination on one momentarily-unavailable file. Annotate
                // any surviving failure with the file/operation so the status names the culprit.
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
                    throw new SyncOperationException(operation, exception);
                }

                if (operation is CopyOperation or MoveOperation)
                {
                    filesCopied++;
                }
            }

            return new DestinationSyncStatus(
                destination.Id, SyncOutcome.Success, DateTimeOffset.UtcNow, filesCopied, Error: null);
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
