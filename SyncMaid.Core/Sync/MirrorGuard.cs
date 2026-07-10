namespace SyncMaid.Core.Sync;

/// <summary>How a Mirror run's planned deletions were judged before applying them.</summary>
public enum MirrorGuardVerdict
{
    /// <summary>Safe to proceed — apply the plan as-is.</summary>
    Allowed,

    /// <summary>The source is empty/unavailable; deletions are refused outright (a blip must
    /// never wipe the backup). Not overridable — the user should fix the source.</summary>
    EmptySource,

    /// <summary>The run would delete a large fraction of the destination. Blocked pending the
    /// user's explicit confirmation; overridable for a single run.</summary>
    NeedsConfirmation,
}

/// <summary>
/// Guards a Mirror run against catastrophic deletion. Mirror deletes every destination
/// file not present in the filtered source set — which is correct, until the source
/// briefly disappears (an unmounted drive, a permission hiccup) and enumerates as empty,
/// or a filter change removes most of the set. Judges a plan's deletions before any are
/// applied. Pure and side-effect-free, so it is exhaustively testable.
/// </summary>
public static class MirrorGuard
{
    /// <summary>
    /// The ratio guard is only meaningful once a destination holds a non-trivial number of
    /// files; below this, deleting "most" of a handful of files is normal, not alarming.
    /// </summary>
    public const int MinDestinationFilesForRatioGuard = 10;

    /// <summary>
    /// Judges whether a Mirror run's deletions are safe to apply.
    /// </summary>
    /// <param name="deleteCount">How many files the plan would delete.</param>
    /// <param name="destinationFileCount">How many files the destination currently holds.</param>
    /// <param name="sourceIsEmpty">True when the effective filtered source contains zero files.</param>
    /// <param name="massDeleteThreshold">
    /// Fraction (0–1) of the destination that, if exceeded, needs confirmation. 0 disables the ratio guard.
    /// </param>
    /// <param name="overrideMassDelete">
    /// When true, a mass delete the user has already confirmed proceeds. Does <b>not</b>
    /// override the empty-source guard.
    /// </param>
    public static MirrorGuardVerdict Evaluate(
        int deleteCount,
        int destinationFileCount,
        bool sourceIsEmpty,
        double massDeleteThreshold,
        bool overrideMassDelete = false)
    {
        if (deleteCount == 0)
        {
            return MirrorGuardVerdict.Allowed;
        }

        // An empty/unavailable source must never drive deletions — this is the blip-wipe case.
        if (sourceIsEmpty)
        {
            return MirrorGuardVerdict.EmptySource;
        }

        // Mass-delete: a large fraction of a non-trivial destination needs confirmation,
        // unless the user has explicitly confirmed this run.
        if (!overrideMassDelete
            && massDeleteThreshold > 0
            && destinationFileCount >= MinDestinationFilesForRatioGuard
            && deleteCount >= destinationFileCount * massDeleteThreshold)
        {
            return MirrorGuardVerdict.NeedsConfirmation;
        }

        return MirrorGuardVerdict.Allowed;
    }
}
