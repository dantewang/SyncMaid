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
    Task<IReadOnlyList<DestinationSyncStatus>> ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null);
}
