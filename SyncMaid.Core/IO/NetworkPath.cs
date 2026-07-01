namespace SyncMaid.Core.IO;

/// <summary>
/// Detects whether a path lives on a mounted network location (a UNC path or a mapped
/// network drive). Used to adapt behaviour that differs on network shares — e.g. no Recycle
/// Bin, and an unreliable <see cref="System.IO.FileSystemWatcher"/> that we replace with
/// polling.
/// </summary>
public static class NetworkPath
{
    /// <summary>True when <paramref name="path"/> is a UNC path or resolves to a network drive.</summary>
    public static bool IsNetwork(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
