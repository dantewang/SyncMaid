using SyncMaid.Core.IO;

namespace SyncMaid.Core.Sync;

/// <summary>
/// An <see cref="ISourceFile"/> backed by a local/mounted filesystem path. Exposes
/// <see cref="LocalPath"/> so a path destination can use an OS copy, and <see cref="OpenRead"/>
/// so a remote destination can stream.
/// </summary>
public sealed class LocalSourceFile : ISourceFile
{
    private readonly IFileSystem _fileSystem;

    public LocalSourceFile(IFileSystem fileSystem, string relativePath, string localPath)
    {
        _fileSystem = fileSystem;
        RelativePath = relativePath;
        LocalPath = localPath;
    }

    public string RelativePath { get; }
    public string? LocalPath { get; }
    public long Length => Stamp.Length;
    public FileStamp Stamp => _fileSystem.GetStamp(LocalPath!);
    public Stream OpenRead() => _fileSystem.OpenRead(LocalPath!);
}
