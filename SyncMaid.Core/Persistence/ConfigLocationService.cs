using System.Text;
using SyncMaid.Core.IO;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Default <see cref="IConfigLocationService"/>. The mode is derived from the presence of a
/// marker file (given by <c>markerPath</c>): present → portable, absent → app-data. Migration
/// is copy-verify, marker flip, then best-effort source cleanup, so an interruption never
/// removes data from the location selected by the marker.
/// </summary>
public sealed class ConfigLocationService : IConfigLocationService
{
    // The config files that move with the location, plus their .bak siblings. The live log is
    // deliberately excluded: it is reopened at the new location on restart (a fresh log there),
    // and moving an in-use file is fragile.
    private static readonly string[] BaseNames = ["tasks.json", "status.json", "settings.json"];

    private readonly IFileSystem _fileSystem;
    private readonly string _appDataDirectory;
    private readonly string _portableDirectory;
    private readonly string _markerPath;

    private ConfigLocationMode _currentMode;

    public ConfigLocationService(IFileSystem fileSystem, string appDataDirectory, string portableDirectory, string markerPath)
    {
        _fileSystem = fileSystem;
        _appDataDirectory = appDataDirectory;
        _portableDirectory = portableDirectory;
        _markerPath = markerPath;
        _currentMode = fileSystem.FileExists(markerPath) ? ConfigLocationMode.Portable : ConfigLocationMode.AppData;
    }

    public ConfigLocationMode CurrentMode => _currentMode;

    public string CurrentDirectory => DirectoryFor(_currentMode);

    public string DirectoryFor(ConfigLocationMode mode) =>
        mode == ConfigLocationMode.Portable ? _portableDirectory : _appDataDirectory;

    public bool CanUse(ConfigLocationMode mode)
    {
        if (mode == _currentMode)
        {
            return true;
        }

        var directory = DirectoryFor(mode);
        var probe = $"{directory}/.syncmaid-writetest";
        try
        {
            _fileSystem.EnsureDirectory(directory);
            using (var stream = _fileSystem.CreateWriteThrough(probe))
            {
                stream.Write([0], 0, 1);
                stream.Flush();
            }

            _fileSystem.DeleteFile(probe);
            return true;
        }
        catch
        {
            _fileSystem.DeleteFile(probe); // best-effort cleanup if the write half-succeeded
            return false;
        }
    }

    public bool SwitchTo(ConfigLocationMode mode)
    {
        if (mode == _currentMode)
        {
            return true;
        }

        if (!CanUse(mode))
        {
            return false;
        }

        var source = DirectoryFor(_currentMode);
        var target = DirectoryFor(mode);

        try
        {
            _fileSystem.EnsureDirectory(target);

            // Phase 1: copy + verify every file. Nothing is deleted yet, so if any copy fails
            // we abort with all sources intact (targets may hold harmless duplicates).
            var moved = new List<string>();
            foreach (var name in Migratable())
            {
                var sourceFile = $"{source}/{name}";
                if (!_fileSystem.FileExists(sourceFile))
                {
                    continue;
                }

                var bytes = _fileSystem.ReadAllBytes(sourceFile);
                var targetFile = $"{target}/{name}";
                _fileSystem.WriteAllBytes(targetFile, bytes);

                if (!_fileSystem.FileExists(targetFile) || !_fileSystem.ReadAllBytes(targetFile).AsSpan().SequenceEqual(bytes))
                {
                    return false; // verification failed — leave everything where it is
                }

                moved.Add(sourceFile);
            }

            // Phase 2: copies verified — select the populated target before touching sources.
            try
            {
                if (mode == ConfigLocationMode.Portable)
                {
                    _fileSystem.WriteAllBytes(_markerPath, MarkerContents);
                }
                else
                {
                    _fileSystem.DeleteFile(_markerPath);
                }
            }
            catch
            {
                // Some filesystem APIs can commit a write/delete and then surface a late
                // flush or handle-close failure. Marker presence is the persisted source of
                // truth, so reconcile with disk before deciding whether the switch failed.
                var markerSelectsRequestedMode = _fileSystem.FileExists(_markerPath)
                    == (mode == ConfigLocationMode.Portable);
                if (!markerSelectsRequestedMode)
                {
                    return false;
                }
            }

            _currentMode = mode;

            // Phase 3: clean up old copies. Once the marker points at the target, leftovers
            // are harmless and must not turn a successful switch into apparent data loss.
            foreach (var sourceFile in moved)
            {
                try
                {
                    _fileSystem.DeleteFile(sourceFile);
                }
                catch
                {
                    // Best effort: the active target already contains the verified copy.
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> Migratable()
    {
        foreach (var name in BaseNames)
        {
            yield return name;
            yield return name + AtomicFile.BackupSuffix;
        }
    }

    private static byte[] MarkerContents =>
        Encoding.UTF8.GetBytes("SyncMaid portable mode: config/data lives in the Data folder beside this file.\n" +
                               "Delete this file to move everything back to %APPDATA%\\SyncMaid.\n");
}
