using System.Text.Json.Serialization;

namespace SyncMaid.Core.Model;

/// <summary>The outcome of a destination's most recent sync.</summary>
public enum SyncOutcome
{
    /// <summary>Has not been synced yet.</summary>
    Never,

    /// <summary>A sync is currently in progress (transient — not persisted).</summary>
    Running,

    /// <summary>The last sync completed successfully.</summary>
    Success,

    /// <summary>
    /// The last sync applied everything it could, but left one or more files for the next
    /// run because they were in use — still being written, or held open by another
    /// process. Nothing is wrong; the destination is simply not caught up yet. See
    /// <see cref="DestinationSyncStatus.FilesDeferred"/>.
    /// </summary>
    Incomplete,

    /// <summary>The last sync failed; see <see cref="DestinationSyncStatus.Error"/>.</summary>
    Failed,

    /// <summary>A Mirror run was blocked by the mass-delete guard and is awaiting the user's
    /// confirmation before it will delete files (see <see cref="Sync.MirrorGuard"/>).</summary>
    NeedsConfirmation,
}

/// <summary>
/// The persisted result of a destination's last sync, keyed by the destination's
/// stable <see cref="Destination.Id"/> so it survives renames and path changes.
/// </summary>
public sealed record DestinationSyncStatus(
    Guid DestinationId,
    SyncOutcome Outcome,
    DateTimeOffset? LastRun = null,
    int FilesCopied = 0,
    string? Error = null)
{
    /// <summary>
    /// The relative paths a successful run actually copied (or moved). Transient run
    /// detail so the UI can count <b>distinct</b> files across a burst of coalesced
    /// runs; never persisted — <c>status.json</c> keeps only the count.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> CopiedRelativePaths { get; init; } = [];

    /// <summary>How many files this run left for the next one because they were in use
    /// (still being written, or held open by another process).</summary>
    public int FilesDeferred { get; init; }

    /// <summary>The relative paths that were deferred. Transient run detail — logged so
    /// the culprit is identifiable; <c>status.json</c> keeps only the count.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> DeferredRelativePaths { get; init; } = [];

    /// <summary>The "not yet run" status for a destination.</summary>
    public static DestinationSyncStatus Never(Guid destinationId) =>
        new(destinationId, SyncOutcome.Never, LastRun: null, FilesCopied: 0, Error: null);
}
