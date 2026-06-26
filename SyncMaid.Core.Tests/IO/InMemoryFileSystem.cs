using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

/// <summary>
/// A disk-free <see cref="IFileSystem"/> for tests. Files live in a dictionary keyed
/// by a normalized absolute path; each holds its bytes and a <see cref="FileStamp"/>.
/// There is no real directory tree — directories are implied by file paths — so
/// <see cref="EnsureDirectory"/> is a no-op and enumeration is by path prefix.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private sealed record Entry(byte[] Contents, FileStamp Stamp);

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seeds a file with explicit contents and stamp; used to set up test state.</summary>
    public void AddFile(string path, byte[] contents, FileStamp stamp)
    {
        _files[Normalize(path)] = new Entry(contents, stamp);
    }

    /// <summary>Convenience overload: stamp derived from length and a fixed default time.</summary>
    public void AddFile(string path, string contents, DateTime? lastWriteUtc = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        var stamp = FileStamp.Create(bytes.Length, lastWriteUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddFile(path, bytes, stamp);
    }

    /// <summary>All file paths currently present (normalized), for assertions.</summary>
    public IReadOnlyCollection<string> AllPaths => _files.Keys.ToList();

    public IEnumerable<string> EnumerateFiles(string root)
    {
        var prefix = Normalize(root).TrimEnd('/') + "/";
        foreach (var path in _files.Keys)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return path[prefix.Length..];
            }
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public FileStamp GetStamp(string path)
    {
        if (_files.TryGetValue(Normalize(path), out var entry))
        {
            return entry.Stamp;
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public byte[] ReadAllBytes(string path)
    {
        if (_files.TryGetValue(Normalize(path), out var entry))
        {
            return entry.Contents;
        }

        throw new FileNotFoundException("No such file in the in-memory filesystem.", path);
    }

    public void WriteAllBytes(string path, byte[] contents)
    {
        var stamp = FileStamp.Create(contents.Length, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _files[Normalize(path)] = new Entry(contents, stamp);
    }

    public void CopyFile(string sourcePath, string destinationPath)
    {
        var source = _files[Normalize(sourcePath)];
        // Mirror PhysicalFileSystem: the copy preserves the source stamp so the pair
        // is not seen as changed on the next run.
        _files[Normalize(destinationPath)] = source with { };
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        var key = Normalize(sourcePath);
        var source = _files[key];
        _files[Normalize(destinationPath)] = source;
        _files.Remove(key);
    }

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public void EnsureDirectory(string path)
    {
        // No real directory tree; directories are implied by file paths.
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
