using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Runs a <see cref="SyncTask"/>. Abstracted so callers (e.g. view models) depend on the
/// contract rather than the concrete <see cref="SyncEngine"/>, which keeps them testable.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Executes <paramref name="task"/> against every destination, honoring
    /// <paramref name="cancellationToken"/> and reporting <paramref name="progress"/>.
    /// </summary>
    Task ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null);
}
