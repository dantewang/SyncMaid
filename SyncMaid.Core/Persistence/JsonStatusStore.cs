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
        var list = JsonConfigFile.TryLoadWithBackup(
            _fileSystem, _path, TaskStoreJsonContext.Default.ListDestinationSyncStatus);
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

    /// <inheritdoc />
    public void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses)
    {
        var list = new List<DestinationSyncStatus>(statuses.Values);
        JsonConfigFile.Save(
            _fileSystem, _path, list, TaskStoreJsonContext.Default.ListDestinationSyncStatus);
    }
}
