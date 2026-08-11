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
    /// sibling temp (write-through), snapshot the previous version as <c>.bak</c>, then
    /// commit with an atomic rename. On any failure the existing file is left untouched,
    /// and at no point does <paramref name="path"/> stop existing.
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

            SnapshotBackup(fileSystem, path);

            // Commit: one atomic rename over the live file.
            fileSystem.Replace(temp, path);
        }
        catch
        {
            TryCleanUp(fileSystem, temp);
            throw;
        }
    }

    // Copies the current good version aside, then renames the copy into place as .bak.
    //
    // The obvious implementation — rename the live file to .bak — is wrong: a rename is a
    // move, so between it and the commit below there is a window in which `path` does not
    // exist at all. A crash there (or a failure of the commit rename, which a reader,
    // an antivirus hold or a sharing violation can all cause) would leave the config with
    // only a .bak, which for tasks.json means the app starts with no visible tasks.
    // Copying keeps the live file in place until the single atomic commit replaces it,
    // and renaming the *copy* keeps the guarantee that .bak is never a partial write.
    private static void SnapshotBackup(IFileSystem fileSystem, string path)
    {
        if (!fileSystem.FileExists(path))
        {
            return; // first save: nothing to back up
        }

        var backupTemp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            var current = fileSystem.ReadAllBytes(path);
            using (var stream = fileSystem.CreateWriteThrough(backupTemp))
            {
                stream.Write(current, 0, current.Length);
                stream.Flush();
            }

            fileSystem.Replace(backupTemp, path + BackupSuffix);
        }
        catch
        {
            TryCleanUp(fileSystem, backupTemp);
            throw;
        }
    }

    private static void TryCleanUp(IFileSystem fileSystem, string temp)
    {
        try
        {
            fileSystem.DeleteFile(temp);
        }
        catch
        {
            // Preserve the write failure that brought us here; cleanup is best-effort and
            // must not replace the caller's real cause with a bogus one.
        }
    }
}
