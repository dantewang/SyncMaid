using System.Text;
using System.Text.Json;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// An <see cref="ISettingsStore"/> that persists <see cref="AppSettings"/> as a single JSON
/// file via the source-generated <see cref="TaskStoreJsonContext"/> (AOT-safe), through
/// <see cref="IFileSystem"/> so it is testable against the in-memory fake. Missing or corrupt
/// settings fall back to the <c>.bak</c> snapshot and finally to defaults, so a bad file never
/// blocks startup.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _path;

    public JsonSettingsStore(IFileSystem fileSystem, string path)
    {
        _fileSystem = fileSystem;
        _path = path;
    }

    /// <inheritdoc />
    public AppSettings Load() =>
        // Fall back to the .bak snapshot if the main file is missing or corrupt, then to defaults.
        TryLoad(_path) ?? TryLoad(_path + AtomicFile.BackupSuffix) ?? new AppSettings();

    private AppSettings? TryLoad(string path)
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
            return JsonSerializer.Deserialize(json, TaskStoreJsonContext.Default.AppSettings);
        }
        catch (JsonException)
        {
            return null; // corrupt file — let the caller try the backup
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, TaskStoreJsonContext.Default.AppSettings);
        AtomicFile.Write(_fileSystem, _path, Encoding.UTF8.GetBytes(json));
    }
}
