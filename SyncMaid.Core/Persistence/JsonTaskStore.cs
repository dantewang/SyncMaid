using System.Text;
using System.Text.Json;
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
        // Fall back to the .bak snapshot if the main file is missing or corrupt.
        TryLoad(_path) ?? TryLoad(_path + AtomicFile.BackupSuffix) ?? [];

    private IReadOnlyList<SyncTask>? TryLoad(string path)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        var json = Encoding.UTF8.GetString(_fileSystem.ReadAllBytes(path));
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, TaskStoreJsonContext.Default.ListSyncTask);
        }
        catch (JsonException)
        {
            return null; // corrupt file — let the caller try the backup
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<SyncTask> tasks)
    {
        // The source-generated contract is typed to List<SyncTask>; materialize once.
        var list = tasks as List<SyncTask> ?? [.. tasks];
        var json = JsonSerializer.Serialize(list, TaskStoreJsonContext.Default.ListSyncTask);
        AtomicFile.Write(_fileSystem, _path, Encoding.UTF8.GetBytes(json));
    }
}
