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

    /// <summary>The last sync failed; see <see cref="DestinationSyncStatus.Error"/>.</summary>
    Failed,
}

/// <summary>
/// The persisted result of a destination's last sync, keyed by the destination's
/// stable <see cref="Destination.Id"/> so it survives renames and path changes.
/// </summary>
public sealed record DestinationSyncStatus(
    Guid DestinationId,
    SyncOutcome Outcome,
    DateTimeOffset? LastRun,
    int FilesCopied,
    string? Error)
{
    /// <summary>The "not yet run" status for a destination.</summary>
    public static DestinationSyncStatus Never(Guid destinationId) =>
        new(destinationId, SyncOutcome.Never, LastRun: null, FilesCopied: 0, Error: null);
}
