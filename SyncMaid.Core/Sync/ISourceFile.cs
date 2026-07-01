using SyncMaid.Core.IO;

namespace SyncMaid.Core.Sync;

/// <summary>
/// A single source file handed to an <see cref="IDestinationProvider"/> to write. The
/// provider chooses the most efficient path for its backend: a local/path provider uses
/// <see cref="LocalPath"/> for an OS copy (and copy-offload); a remote provider streams via
/// <see cref="OpenRead"/>. In phase 1 the source is always a local path, so
/// <see cref="LocalPath"/> is always present.
/// </summary>
public interface ISourceFile
{
    /// <summary>The file's path relative to the source root (forward slashes).</summary>
    string RelativePath { get; }

    /// <summary>The absolute source path when the source is a local/mounted filesystem; otherwise null.</summary>
    string? LocalPath { get; }

    /// <summary>The source file's length in bytes.</summary>
    long Length { get; }

    /// <summary>The source file's change-detection stamp (size + last-write-time).</summary>
    FileStamp Stamp { get; }

    /// <summary>Opens the source file for reading. The caller disposes the stream.</summary>
    Stream OpenRead();
}
