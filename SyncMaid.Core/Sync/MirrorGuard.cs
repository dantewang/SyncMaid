namespace SyncMaid.Core.Sync;

/// <summary>
/// Guards a Mirror run against catastrophic deletion. Mirror deletes every destination
/// file not present in the filtered source set — which is correct, until the source
/// briefly disappears (an unmounted drive, a permission hiccup) and enumerates as empty,
/// turning the plan into "delete the entire backup." This validates a plan's deletions
/// before any are applied. Pure and side-effect-free, so it is exhaustively testable.
/// </summary>
public static class MirrorGuard
{
    /// <summary>
    /// The ratio guard is only meaningful once a destination holds a non-trivial number of
    /// files; below this, deleting "most" of a handful of files is normal, not alarming.
    /// </summary>
    public const int MinDestinationFilesForRatioGuard = 10;

    /// <summary>
    /// Throws <see cref="MirrorGuardException"/> when the planned deletions look dangerous.
    /// </summary>
    /// <param name="deleteCount">How many files the plan would delete.</param>
    /// <param name="destinationFileCount">How many files the destination currently holds.</param>
    /// <param name="sourceIsEmpty">True when the source enumerated to zero files (missing or empty).</param>
    /// <param name="massDeleteThreshold">
    /// Fraction (0–1) of the destination that, if exceeded, aborts the run. 0 disables the ratio guard.
    /// </param>
    public static void Validate(
        int deleteCount,
        int destinationFileCount,
        bool sourceIsEmpty,
        double massDeleteThreshold)
    {
        if (deleteCount == 0)
        {
            return;
        }

        // An empty/unavailable source must never drive deletions — this is the blip-wipe case.
        if (sourceIsEmpty)
        {
            throw new MirrorGuardException(
                $"The source is empty or unavailable; skipping {deleteCount} deletion(s) to avoid wiping the destination.");
        }

        // Mass-delete: refuse to remove a large fraction of a non-trivial destination.
        if (massDeleteThreshold > 0
            && destinationFileCount >= MinDestinationFilesForRatioGuard
            && deleteCount >= destinationFileCount * massDeleteThreshold)
        {
            throw new MirrorGuardException(
                $"Refusing to delete {deleteCount} of {destinationFileCount} files " +
                $"(at or over the {massDeleteThreshold:P0} safety threshold). Confirm the change to proceed.");
        }
    }
}
