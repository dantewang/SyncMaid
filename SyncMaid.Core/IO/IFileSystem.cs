namespace SyncMaid.Core.IO;

/// <summary>
/// The filesystem operations the sync engine needs, abstracted so the engine can
/// run against a real disk (<see cref="PhysicalFileSystem"/>) or an in-memory fake
/// in tests. All paths are absolute unless noted; relative paths are always
/// relative to a given root and use forward slashes.
/// </summary>
/// <remarks>
/// Implementations must be trim/AOT-safe: no reflection, no dynamic code.
/// </remarks>
public interface IFileSystem
{
    /// <summary>
    /// Enumerates every file under <paramref name="root"/> (recursively) as paths
    /// relative to that root, using forward slashes. Returns an empty sequence when
    /// the root does not exist. Directories themselves are not yielded.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string root);

    /// <summary>True when a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>
    /// Returns a <see cref="FileStamp"/> for the file at <paramref name="path"/>.
    /// Throws if the file does not exist.
    /// </summary>
    FileStamp GetStamp(string path);

    /// <summary>Reads the full contents of the file at <paramref name="path"/>.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/>, overwriting any
    /// existing file and creating parent directories as needed.
    /// </summary>
    void WriteAllBytes(string path, byte[] contents);

    /// <summary>
    /// Copies the file at <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>, overwriting the destination and creating
    /// its parent directories as needed. Preserves the source last-write-time so the
    /// copies share a <see cref="FileStamp"/>.
    /// </summary>
    void CopyFile(string sourcePath, string destinationPath);

    /// <summary>
    /// Moves the file at <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>, overwriting the destination and creating
    /// its parent directories as needed.
    /// </summary>
    void MoveFile(string sourcePath, string destinationPath);

    /// <summary>Deletes the file at <paramref name="path"/> if it exists; otherwise no-op.</summary>
    void DeleteFile(string path);

    /// <summary>Ensures the directory at <paramref name="path"/> exists, creating parents as needed.</summary>
    void EnsureDirectory(string path);
}
