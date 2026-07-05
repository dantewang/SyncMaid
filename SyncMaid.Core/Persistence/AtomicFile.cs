using SyncMaid.Core.IO;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Writes a config file safely: a crash or power cut mid-write must never corrupt it (for
/// <c>tasks.json</c> that would mean losing every task definition). The same
/// temp → flush → atomic-rename discipline the sync engine uses for user files, applied to
/// our own config: the destination is only ever replaced by a complete file, and the
/// previous version is kept as <c>&lt;path&gt;.bak</c> so a corrupt main file can be recovered.
/// </summary>
public static class AtomicFile
{
    /// <summary>The suffix of the previous-version backup written alongside the main file.</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>
    /// Atomically writes <paramref name="contents"/> to <paramref name="path"/>: write a
    /// sibling temp (write-through), keep the previous version as <c>.bak</c>, then commit
    /// with an atomic rename. On any failure the existing file is left untouched.
    /// </summary>
    public static void Write(IFileSystem fileSystem, string path, byte[] contents)
    {
        var temp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = fileSystem.CreateWriteThrough(temp))
            {
                stream.Write(contents, 0, contents.Length);
                stream.Flush();
            }

            // Preserve the current good version as .bak (atomic rename → the backup is always
            // a complete file, never a partial write). Then commit the new version atomically.
            if (fileSystem.FileExists(path))
            {
                fileSystem.Replace(path, path + BackupSuffix);
            }

            fileSystem.Replace(temp, path);
        }
        catch
        {
            fileSystem.DeleteFile(temp);
            throw;
        }
    }
}
