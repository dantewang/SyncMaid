using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Loads and saves destination sync statuses, keyed by destination id. Kept separate
/// from <see cref="ITaskStore"/> so runtime status doesn't churn the task config file.
/// </summary>
public interface IStatusStore
{
    /// <summary>Loads all saved statuses by destination id. Empty on first run.</summary>
    IReadOnlyDictionary<Guid, DestinationSyncStatus> Load();

    /// <summary>Persists the given statuses, replacing any previously saved set.</summary>
    void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses);
}
