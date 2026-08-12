namespace SyncMaid.Core.Sync;

/// <summary>
/// The operations produced by planning plus destination metadata gathered by the same
/// snapshot walk. The engine uses the count for MirrorGuard without re-enumerating.
/// </summary>
public sealed record SyncPlan(
    IReadOnlyList<SyncOperation> Operations,
    int DestinationFileCount)
{
    /// <summary>
    /// Source-relative paths a flattening Move destination refused to move because their
    /// name was already taken and its policy is
    /// <see cref="Model.FileNameCollisionPolicy.Skip"/>. They stay in the source, so the
    /// engine reports them rather than letting the run look complete.
    /// </summary>
    public IReadOnlyList<string> SkippedCollisions { get; init; } = [];
}
