namespace SyncMaid.Core.Sync;

/// <summary>
/// A preview of the deletions a Mirror destination would make right now — used to show the
/// user what a confirmed mass-delete will remove before they approve it.
/// </summary>
/// <param name="Count">Total files that would be deleted.</param>
/// <param name="Sample">A bounded sample of the relative paths, for display.</param>
public sealed record MirrorDeletePreview(int Count, IReadOnlyList<string> Sample)
{
    /// <summary>An empty preview — nothing to delete.</summary>
    public static MirrorDeletePreview None { get; } = new(0, []);
}
