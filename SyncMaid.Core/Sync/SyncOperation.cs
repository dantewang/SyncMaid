namespace SyncMaid.Core.Sync;

/// <summary>
/// A single planned change against a destination. Produced by <see cref="SyncPlanner"/>
/// without touching the filesystem, then executed by <see cref="SyncApplier"/> against an
/// <see cref="IDestinationProvider"/>. The destination file is addressed by its
/// <c>RelativePath</c> (forward slashes) — the provider knows how to place it, so operations
/// carry no absolute destination path and work for any backend.
/// </summary>
/// <remarks>
/// Closed hierarchy: the applier switches over the concrete types exhaustively, so
/// there is no reflection and the engine stays AOT/trim-safe.
/// </remarks>
public abstract record SyncOperation(string RelativePath);

/// <summary>
/// Copy a file from the source into the destination, overwriting any existing copy.
/// Used by Mirror and AddOnly for new and changed files. <c>SourceFullPath</c> is the
/// absolute local source path.
/// </summary>
public sealed record CopyOperation(string RelativePath, string SourceFullPath)
    : SyncOperation(RelativePath)
{
    /// <summary>When true, the copy is read back and content-verified before commit (§ Destination.VerifyContents).</summary>
    public bool Verify { get; init; }
}

/// <summary>
/// Delete a file from the destination. Emitted only by Mirror, for destination files
/// that are no longer in the filtered source set.
/// </summary>
public sealed record DeleteOperation(string RelativePath)
    : SyncOperation(RelativePath)
{
    /// <summary>Whether the file is sent to the Recycle Bin or deleted permanently.</summary>
    public Model.DeleteMode Mode { get; init; }
}

/// <summary>
/// Create a destination directory so the destination replicates the source directory
/// tree even where no files exist. Emitted only by Mirror, before its copies,
/// parents-first. Idempotent: an existing directory is left as-is.
/// </summary>
public sealed record CreateDirectoryOperation(string RelativePath)
    : SyncOperation(RelativePath);

/// <summary>
/// Remove a destination directory that no longer exists in the source. Emitted only by
/// Mirror, after its <see cref="DeleteOperation"/>s, deepest-first, never for the
/// destination root. Applied best-effort and non-recursively: a directory that gained
/// content since planning (or already vanished) is skipped, so unknown content is
/// never deleted.
/// </summary>
public sealed record DeleteDirectoryOperation(string RelativePath)
    : SyncOperation(RelativePath);

/// <summary>
/// Set a destination directory's last-write time to the source directory's, so tree
/// identity extends to directory metadata. Emitted only by Mirror, and last in the
/// plan: entry creates/deletes bump the parent directory's time, and NTFS does not
/// bump a parent when a child's own timestamps change, so one trailing pass converges.
/// Applied best-effort: a directory that vanished since planning is skipped.
/// </summary>
public sealed record SetDirectoryTimestampOperation(string RelativePath, DateTime LastWriteTimeUtc)
    : SyncOperation(RelativePath);

/// <summary>
/// Move a file from the source into the destination: copy it across, then remove the
/// source. Emitted only by the Move strategy. <c>SourceFullPath</c> is the absolute local
/// source path.
/// </summary>
public sealed record MoveOperation(string RelativePath, string SourceFullPath)
    : SyncOperation(RelativePath)
{
    /// <summary>When true, the copy is read back and content-verified before the source is deleted.</summary>
    public bool Verify { get; init; }
}
