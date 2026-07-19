namespace SyncMaid.Core.IO;

/// <summary>A file seen by one tree walk: its root-relative path (forward slashes) and
/// change-detection <see cref="FileStamp"/>.</summary>
public readonly record struct ListedFile(string RelativePath, FileStamp Stamp);

/// <summary>A directory seen by one tree walk: its root-relative path (forward slashes)
/// and last-write time (normalized like <see cref="FileStamp"/> timestamps), so Mirror
/// can preserve directory modified times the way copies preserve file times.</summary>
public readonly record struct ListedDirectory(string RelativePath, DateTime LastWriteTimeUtc);

/// <summary>
/// The result of walking a tree once: every file with its stamp, and every directory
/// with its modified time, as root-relative paths (forward slashes, the root itself
/// excluded). Produced in a single enumeration pass — stamps and times come from the
/// walk's own directory metadata, so callers never pay a second per-entry metadata
/// round trip for data the listing already carried (one round trip per directory
/// instead of per file on network paths).
/// </summary>
public sealed record TreeListing(IReadOnlyList<ListedFile> Files, IReadOnlyList<ListedDirectory> Directories)
{
    /// <summary>A tree with no files and no directories (e.g. a destination that does not exist yet).</summary>
    public static readonly TreeListing Empty = new([], []);
}
