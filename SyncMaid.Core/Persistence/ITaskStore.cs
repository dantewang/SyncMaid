using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Loads and saves the user's configured sync tasks so they survive restarts.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Loads the persisted tasks. Returns an empty list when nothing has been saved yet
    /// (first run), rather than throwing.
    /// </summary>
    IReadOnlyList<SyncTask> Load();

    /// <summary>Persists <paramref name="tasks"/>, replacing any previously saved set.</summary>
    void Save(IReadOnlyList<SyncTask> tasks);
}
