namespace SyncMaid.Core.IO;

/// <summary>
/// An <see cref="IFileSystem"/> backed by <see cref="System.IO"/>, operating on the
/// real disk. Relative paths returned by <see cref="EnumerateFiles"/> use forward
/// slashes regardless of platform so they compare consistently with the rest of the
/// engine and the filter rules.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var fullRoot = Path.GetFullPath(root);
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fullRoot, file);
            yield return relative.Replace('\\', '/');
        }
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public FileStamp GetStamp(string path)
    {
        var info = new FileInfo(path);
        return FileStamp.Create(info.Length, info.LastWriteTimeUtc);
    }

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] contents)
    {
        EnsureParentDirectory(path);
        File.WriteAllBytes(path, contents);
    }

    /// <inheritdoc />
    public void CopyFile(string sourcePath, string destinationPath)
    {
        EnsureParentDirectory(destinationPath);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        // Preserve the source timestamp so source and copy share a FileStamp and are
        // not seen as "changed" on the next run.
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath)
    {
        EnsureParentDirectory(destinationPath);
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
