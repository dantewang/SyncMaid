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
public sealed class SyncEngine
{
    private readonly IFileSystem _fileSystem;

    public SyncEngine(IFileSystem fileSystem) => _fileSystem = fileSystem;

    /// <summary>
    /// Executes <paramref name="task"/> against every destination, in order. Honors
    /// <paramref name="cancellationToken"/> between operations and reports a
    /// <see cref="SyncProgress"/> before applying each operation.
    /// </summary>
    /// <remarks>
    /// The work is synchronous filesystem I/O; we wrap it on a background thread via
    /// <see cref="Task.Run(Action, CancellationToken)"/> so callers (e.g. a UI) stay
    /// responsive and can await completion.
    /// </remarks>
    public Task ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null)
    {
        return Task.Run(() => Execute(task, cancellationToken, progress), cancellationToken);
    }

    private void Execute(SyncTask task, CancellationToken cancellationToken, IProgress<SyncProgress>? progress)
    {
        foreach (var destination in task.Destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filtered = _fileSystem
                .EnumerateFiles(task.SourcePath)
                .Where(destination.Includes)
                .ToList();

            var plan = SyncPlanner.Plan(_fileSystem, task.SourcePath, destination, filtered);

            for (var i = 0; i < plan.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = plan[i];
                progress?.Report(new SyncProgress(destination, operation, i, plan.Count));
                SyncApplier.Apply(_fileSystem, operation);
            }
        }
    }
}
