using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Runs a <see cref="SyncTask"/>. Abstracted so callers (e.g. view models) depend on the
/// contract rather than the concrete <see cref="SyncEngine"/>, which keeps them testable.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Executes <paramref name="task"/> against every destination, returning each
    /// destination's outcome. Honors <paramref name="cancellationToken"/> and reports
    /// <paramref name="progress"/> as operations are applied.
    /// </summary>
    /// <param name="confirmedMassDeletes">
    /// Destination ids whose mass-delete guard the user has confirmed for this run; those
    /// destinations proceed with their deletions. Never persisted; empty by default.
    /// </param>
    Task<IReadOnlyList<DestinationSyncStatus>> ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null,
        IReadOnlySet<Guid>? confirmedMassDeletes = null);

    /// <summary>
    /// Computes the deletions a Mirror destination would make right now (without applying
    /// anything), so the user can review a blocked mass-delete before confirming it.
    /// </summary>
    Task<MirrorDeletePreview> PreviewMirrorDeletionsAsync(
        SyncTask task,
        Guid destinationId,
        CancellationToken cancellationToken = default);
}
