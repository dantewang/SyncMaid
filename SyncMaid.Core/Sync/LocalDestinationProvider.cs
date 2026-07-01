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

    public IEnumerable<string> Enumerate() => _fileSystem.EnumerateFiles(_root);

    public bool Exists(string relativePath) => _fileSystem.FileExists(Full(relativePath));

    public FileStamp GetStamp(string relativePath) => _fileSystem.GetStamp(Full(relativePath));

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

    // Joins the root with a forward-slash relative path; separators are normalized by the
    // filesystem. Mirrors SyncPlanner's path building.
    private string Full(string relativePath)
    {
        var trimmedRoot = _root.TrimEnd('/', '\\');
        return $"{trimmedRoot}/{relativePath}";
    }
}
