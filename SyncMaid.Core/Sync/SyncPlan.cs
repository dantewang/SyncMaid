namespace SyncMaid.Core.Sync;

/// <summary>
/// The operations produced by planning plus destination metadata gathered by the same
/// snapshot walk. The engine uses the count for MirrorGuard without re-enumerating.
/// </summary>
public sealed record SyncPlan(
    IReadOnlyList<SyncOperation> Operations,
    int DestinationFileCount);
