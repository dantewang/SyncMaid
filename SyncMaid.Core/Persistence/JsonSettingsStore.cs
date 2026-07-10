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
        JsonConfigFile.TryLoadWithBackup(
            _fileSystem, _path, TaskStoreJsonContext.Default.AppSettings) ?? new AppSettings();

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        JsonConfigFile.Save(_fileSystem, _path, settings, TaskStoreJsonContext.Default.AppSettings);
    }
}
