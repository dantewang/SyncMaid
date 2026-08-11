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
    /// <remarks>
    /// An empty result is ambiguous: it also means the config could not be read. Anything
    /// that may go on to <see cref="Save"/> must use
    /// <see cref="Load(out bool)"/> instead, or it can persist an empty list over a
    /// config that was merely unavailable.
    /// </remarks>
    IReadOnlyList<SyncTask> Load();

    /// <summary>
    /// Loads the persisted tasks, reporting whether the config was unreadable.
    /// </summary>
    /// <param name="unreadable">
    /// True when a config file is present but neither it nor its backup could be read.
    /// The returned list is empty in that case, but the user's tasks are <b>not</b> gone —
    /// saving over them would be what loses them.
    /// </param>
    IReadOnlyList<SyncTask> Load(out bool unreadable);

    /// <summary>Persists <paramref name="tasks"/>, replacing any previously saved set.</summary>
    void Save(IReadOnlyList<SyncTask> tasks);
}
