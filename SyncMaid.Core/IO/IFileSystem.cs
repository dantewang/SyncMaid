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
    /// Walks the tree under <paramref name="root"/> once (recursively), returning every
    /// file with its <see cref="FileStamp"/> and every directory as a
    /// <see cref="TreeListing"/>. A root that does not exist throws rather than reading
    /// as empty: an unplugged source drive must fail the run, not look like an empty
    /// source.
    /// </summary>
    TreeListing ListTree(string root);

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

    /// <summary>Deletes the file at <paramref name="path"/> if it exists; otherwise no-op.</summary>
    void DeleteFile(string path);

    /// <summary>
    /// Sends the file at <paramref name="path"/> to the Recycle Bin if it exists, so the
    /// deletion is recoverable. On volumes without a Recycle Bin (e.g. network shares) this
    /// falls back to a permanent delete.
    /// </summary>
    void Recycle(string path);

    /// <summary>Ensures the directory at <paramref name="path"/> exists, creating parents as needed.</summary>
    void EnsureDirectory(string path);

    /// <summary>
    /// Deletes the directory at <paramref name="path"/> only if it is empty; a directory
    /// that has content or does not exist is left alone without error. Never recursive.
    /// </summary>
    void DeleteEmptyDirectory(string path);

    /// <summary>
    /// Sets the last-write time (UTC) of the directory at <paramref name="path"/>, so a
    /// mirrored directory can share its source's modified time. A directory that does
    /// not exist is left alone without error (the next run replans).
    /// </summary>
    void SetDirectoryLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc);

    /// <summary>
    /// Opens the file at <paramref name="path"/> for reading. The caller disposes the
    /// stream. Throws if the file does not exist.
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>
    /// Creates (or overwrites) the file at <paramref name="path"/> for writing, creating
    /// parent directories as needed, with write-through semantics so bytes are flushed to
    /// the storage device rather than left in a write cache. The caller disposes the stream.
    /// </summary>
    Stream CreateWriteThrough(string path);

    /// <summary>
    /// Sets the last-write-time (UTC) of the file at <paramref name="path"/>, so a copy can
    /// be made to share its source's <see cref="FileStamp"/>.
    /// </summary>
    void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc);

    /// <summary>
    /// Atomically replaces <paramref name="destinationPath"/> with the file at
    /// <paramref name="sourcePath"/> (an on-volume rename/overwrite), creating the
    /// destination's parent directories as needed. After it returns the source no longer
    /// exists and the destination holds the source's bytes.
    /// </summary>
    void Replace(string sourcePath, string destinationPath);

    /// <summary>
    /// Bytes of free space available on the volume that would hold <paramref name="path"/>.
    /// Used as a preflight before a copy. Returns <see cref="long.MaxValue"/> when the
    /// amount cannot be determined.
    /// </summary>
    long GetAvailableFreeSpace(string path);
}
