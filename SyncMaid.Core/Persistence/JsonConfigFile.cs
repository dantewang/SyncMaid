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
        TryLoad(fileSystem, path, typeInfo)
        ?? TryLoad(fileSystem, path + AtomicFile.BackupSuffix, typeInfo);

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
