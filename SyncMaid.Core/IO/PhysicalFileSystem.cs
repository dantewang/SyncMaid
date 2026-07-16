using System.Runtime.InteropServices;

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
        // A missing root must be distinguishable from an empty one: silently yielding
        // nothing made an unplugged source drive look like an empty source (and Mirror
        // into an empty destination then reported Success). Destinations that don't
        // exist yet are tolerated by LocalDestinationProvider, not here.
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder not found or unavailable: {root}");
        }

        return EnumerateCore(Path.GetFullPath(root));
    }

    private static IEnumerable<string> EnumerateCore(string fullRoot)
    {
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fullRoot, file);
            yield return relative.Replace('\\', '/');
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string root)
    {
        // Same missing-vs-empty distinction as EnumerateFiles: an unavailable root is an
        // error, not an empty tree.
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder not found or unavailable: {root}");
        }

        return EnumerateDirectoriesCore(Path.GetFullPath(root));
    }

    private static IEnumerable<string> EnumerateDirectoriesCore(string fullRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(fullRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fullRoot, directory);
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
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public void Recycle(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Network shares have no Recycle Bin; a permanent delete is the only option there.
        if (NetworkPath.IsNetwork(path))
        {
            File.Delete(path);
            return;
        }

        // SHFileOperation moves the file to the Recycle Bin (FOF_ALLOWUNDO). pFrom must be
        // double-null-terminated. Silent, no confirmation or error UI.
        var shellPath = NormalizeRecyclePath(path);
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = shellPath + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };

        var result = SHFileOperation(ref operation);
        if (result != 0)
        {
            throw new IOException($"Failed to send '{path}' to the Recycle Bin (SHFileOperation returned {result}).");
        }
    }

    internal static string NormalizeRecyclePath(string path) =>
        Path.GetFullPath(path).Replace('/', '\\');

    /// <inheritdoc />
    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void DeleteEmptyDirectory(string path)
    {
        try
        {
            // Non-recursive: a directory that gained content since it was planned for
            // removal throws (not empty) and is kept — unknown content is never deleted.
            Directory.Delete(path, recursive: false);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone (e.g. a parent raced it away); the desired state holds.
        }
        catch (IOException)
        {
            // Not empty or momentarily in use — leave it; cleanup is best-effort.
        }
    }

    /// <inheritdoc />
    public Stream OpenRead(string path) => File.OpenRead(path);

    /// <inheritdoc />
    public Stream CreateWriteThrough(string path)
    {
        EnsureParentDirectory(path);
        // WriteThrough flushes to the storage device on each write rather than leaving
        // bytes in the OS write cache, so a crash right after a "successful" copy can't
        // lose them.
        return new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            FileOptions.WriteThrough);
    }

    /// <inheritdoc />
    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) =>
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);

    /// <inheritdoc />
    public void Replace(string sourcePath, string destinationPath)
    {
        EnsureParentDirectory(destinationPath);
        // On-volume move with overwrite is an atomic rename: the destination flips from
        // the old file to the fully-written new one with no half-written window.
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <inheritdoc />
    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return long.MaxValue;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // UNC paths and some volumes can't be probed via DriveInfo; don't block the
            // copy on a preflight we couldn't run.
            return long.MaxValue;
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    // --- Win32 Recycle Bin interop (shell32 SHFileOperation) ---

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
