using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Runs a <see cref="SyncTask"/>: for each destination it enumerates the source,
/// applies the destination's filters, plans the operations for that destination's
/// strategy, then applies them. Planning and applying are delegated to
/// <see cref="SyncPlanner"/> and <see cref="SyncApplier"/> so this type only
/// orchestrates: enumerate → filter → plan → apply, with cancellation and progress.
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private readonly IFileSystem _fileSystem;

    public SyncEngine(IFileSystem fileSystem) => _fileSystem = fileSystem;

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
        IProgress<SyncProgress>? progress = null)
    {
        return Task.Run(() => Execute(task, cancellationToken, progress), cancellationToken);
    }

    private IReadOnlyList<DestinationSyncStatus> Execute(
        SyncTask task,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress)
    {
        var statuses = new List<DestinationSyncStatus>(task.Destinations.Count);
        foreach (var destination in task.Destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(ExecuteDestination(task, destination, cancellationToken, progress));
        }

        return statuses;
    }

    private DestinationSyncStatus ExecuteDestination(
        SyncTask task,
        Destination destination,
        CancellationToken cancellationToken,
        IProgress<SyncProgress>? progress)
    {
        try
        {
            var sourceFiles = _fileSystem.EnumerateFiles(task.SourcePath).ToList();
            var filtered = sourceFiles.Where(destination.Includes).ToList();

            var plan = SyncPlanner.Plan(_fileSystem, task.SourcePath, destination, filtered);

            // Guard Mirror deletions before applying anything: an empty/unavailable source
            // or a mass-delete must not silently wipe the destination.
            var deleteCount = plan.Count(operation => operation is DeleteOperation);
            if (deleteCount > 0)
            {
                var destinationFileCount = _fileSystem.EnumerateFiles(destination.Path).Count();
                MirrorGuard.Validate(
                    deleteCount,
                    destinationFileCount,
                    sourceIsEmpty: sourceFiles.Count == 0,
                    destination.MassDeleteThreshold);
            }

            var filesCopied = 0;
            for (var i = 0; i < plan.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = plan[i];
                progress?.Report(new SyncProgress(destination, operation, i, plan.Count));
                SyncApplier.Apply(_fileSystem, operation);

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
