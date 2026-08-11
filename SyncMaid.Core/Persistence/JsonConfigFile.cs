using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SyncMaid.Core.IO;

namespace SyncMaid.Core.Persistence;

/// <summary>Shared primary/backup JSON loading and atomic saving for config files.</summary>
internal static class JsonConfigFile
{
    public static T? TryLoadWithBackup<T>(
        IFileSystem fileSystem,
        string path,
        JsonTypeInfo<T> typeInfo)
        where T : class =>
        TryLoadWithBackup(fileSystem, path, typeInfo, out _);

    /// <summary>
    /// As <see cref="TryLoadWithBackup{T}(IFileSystem,string,JsonTypeInfo{T})"/>, but
    /// distinguishes "nothing saved yet" from "saved, but unreadable".
    /// </summary>
    /// <param name="unreadable">
    /// True when a config file is present but neither it nor its backup could be read —
    /// corrupt JSON, or an I/O or permission failure (an antivirus scan, a roaming-profile
    /// lock, a sync client holding the file). Callers must not treat that as "the user has
    /// nothing configured": persisting over it would rotate the last good copy away.
    /// </param>
    public static T? TryLoadWithBackup<T>(
        IFileSystem fileSystem,
        string path,
        JsonTypeInfo<T> typeInfo,
        out bool unreadable)
        where T : class
    {
        var backupPath = path + AtomicFile.BackupSuffix;
        var loaded = TryLoad(fileSystem, path, typeInfo) ?? TryLoad(fileSystem, backupPath, typeInfo);
        unreadable = loaded is null
            && (fileSystem.FileExists(path) || fileSystem.FileExists(backupPath));
        return loaded;
    }

    public static void Save<T>(
        IFileSystem fileSystem,
        string path,
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        AtomicFile.Write(fileSystem, path, Encoding.UTF8.GetBytes(json));
    }

    private static T? TryLoad<T>(IFileSystem fileSystem, string path, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(fileSystem.ReadAllBytes(path));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
