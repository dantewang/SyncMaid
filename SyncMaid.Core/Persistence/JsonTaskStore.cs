using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// An <see cref="ITaskStore"/> that persists tasks as a single JSON file via the
/// source-generated <see cref="TaskStoreJsonContext"/>. Reads and writes go through
/// <see cref="IFileSystem"/> so the store is testable against the in-memory fake.
/// </summary>
public sealed class JsonTaskStore : ITaskStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _path;

    /// <param name="fileSystem">Filesystem to read/write the config file through.</param>
    /// <param name="path">Absolute path of the JSON config file.</param>
    public JsonTaskStore(IFileSystem fileSystem, string path)
    {
        _fileSystem = fileSystem;
        _path = path;
    }

    /// <inheritdoc />
    public IReadOnlyList<SyncTask> Load() =>
        JsonConfigFile.TryLoadWithBackup(
            _fileSystem, _path, TaskStoreJsonContext.Default.ListSyncTask) ?? [];

    /// <inheritdoc />
    public void Save(IReadOnlyList<SyncTask> tasks)
    {
        // The source-generated contract is typed to List<SyncTask>; materialize once.
        var list = tasks as List<SyncTask> ?? [.. tasks];
        JsonConfigFile.Save(_fileSystem, _path, list, TaskStoreJsonContext.Default.ListSyncTask);
    }
}
