using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// An <see cref="IDestinationProvider"/> for a local or mounted (network) path, backed by an
/// <see cref="IFileSystem"/>. Its commit + verification strategy is the atomic
/// <see cref="SafeFileTransfer"/> (temp → verify → atomic rename); deletions go to the
/// Recycle Bin or are permanent per the requested mode (with the filesystem falling back to
/// a permanent delete on volumes without a Recycle Bin, e.g. network shares).
/// </summary>
public sealed class LocalDestinationProvider : IDestinationProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly string _root;

    public LocalDestinationProvider(IFileSystem fileSystem, string root)
    {
        _fileSystem = fileSystem;
        _root = root;
    }

    /// <summary>A path destination is never "remote"; recycling is supported (with a runtime
    /// fallback on network shares handled by the filesystem).</summary>
    public DestinationCapabilities Capabilities => new(IsRemote: false, SupportsRecycle: true);

    // A destination that does not exist yet is an empty destination — the first run
    // creates it. The source side deliberately gets no such tolerance: a missing
    // source must fail the run, not read as empty.
    public TreeListing ListTree()
    {
        try
        {
            return _fileSystem.ListTree(_root);
        }
        catch (DirectoryNotFoundException)
        {
            return TreeListing.Empty;
        }
    }

    public FileStamp GetStamp(string relativePath) => _fileSystem.GetStamp(Full(relativePath));

    public bool TryGetStamp(string relativePath, out FileStamp stamp)
    {
        var fullPath = Full(relativePath);
        if (!_fileSystem.FileExists(fullPath))
        {
            stamp = default;
            return false;
        }

        try
        {
            stamp = _fileSystem.GetStamp(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            stamp = default;
            return false;
        }
    }

    public void Write(string relativePath, ISourceFile source, bool verifyContents)
    {
        // The source is local (LocalPath present), so use the atomic path-to-path copy,
        // which keeps the OS fast path and copy-offload.
        SafeFileTransfer.Copy(_fileSystem, source.LocalPath!, Full(relativePath), verifyContents);
    }

    public void Delete(string relativePath, DeleteMode mode)
    {
        var full = Full(relativePath);
        if (mode == DeleteMode.Recycle)
        {
            _fileSystem.Recycle(full);
        }
        else
        {
            _fileSystem.DeleteFile(full);
        }
    }

    public void EnsureDirectory(string relativePath) =>
        _fileSystem.EnsureDirectory(Full(relativePath));

    public void DeleteEmptyDirectory(string relativePath) =>
        _fileSystem.DeleteEmptyDirectory(Full(relativePath));

    private string Full(string relativePath) => RelativePaths.Join(_root, relativePath);
}
