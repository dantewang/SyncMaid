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
        if (!_fileSystem.FileExists(_path))
        {
            return new Dictionary<Guid, DestinationSyncStatus>();
        }

        var json = Encoding.UTF8.GetString(_fileSystem.ReadAllBytes(_path));
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<Guid, DestinationSyncStatus>();
        }

        var list = JsonSerializer.Deserialize(json, TaskStoreJsonContext.Default.ListDestinationSyncStatus) ?? [];
        var byId = new Dictionary<Guid, DestinationSyncStatus>(list.Count);
        foreach (var status in list)
        {
            byId[status.DestinationId] = status;
        }

        return byId;
    }

    /// <inheritdoc />
    public void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses)
    {
        var list = new List<DestinationSyncStatus>(statuses.Values);
        var json = JsonSerializer.Serialize(list, TaskStoreJsonContext.Default.ListDestinationSyncStatus);
        _fileSystem.WriteAllBytes(_path, Encoding.UTF8.GetBytes(json));
    }
}
