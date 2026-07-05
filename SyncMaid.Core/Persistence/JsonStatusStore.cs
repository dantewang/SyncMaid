using System.Text;
using System.Text.Json;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// An <see cref="IStatusStore"/> that persists statuses as a JSON list via the
/// source-generated <see cref="TaskStoreJsonContext"/> (AOT-safe), through
/// <see cref="IFileSystem"/> so it is testable against the in-memory fake. Stored as a
/// flat list (each status carries its destination id) and indexed on load.
/// </summary>
public sealed class JsonStatusStore : IStatusStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _path;

    public JsonStatusStore(IFileSystem fileSystem, string path)
    {
        _fileSystem = fileSystem;
        _path = path;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, DestinationSyncStatus> Load()
    {
        // Fall back to the .bak snapshot if the main file is missing or corrupt.
        var list = TryLoad(_path) ?? TryLoad(_path + AtomicFile.BackupSuffix);
        if (list is null)
        {
            return new Dictionary<Guid, DestinationSyncStatus>();
        }

        var byId = new Dictionary<Guid, DestinationSyncStatus>(list.Count);
        foreach (var status in list)
        {
            byId[status.DestinationId] = status;
        }

        return byId;
    }

    private List<DestinationSyncStatus>? TryLoad(string path)
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
            return JsonSerializer.Deserialize(json, TaskStoreJsonContext.Default.ListDestinationSyncStatus);
        }
        catch (JsonException)
        {
            return null; // corrupt file — let the caller try the backup
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses)
    {
        var list = new List<DestinationSyncStatus>(statuses.Values);
        var json = JsonSerializer.Serialize(list, TaskStoreJsonContext.Default.ListDestinationSyncStatus);
        AtomicFile.Write(_fileSystem, _path, Encoding.UTF8.GetBytes(json));
    }
}
