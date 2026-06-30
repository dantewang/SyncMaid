using SyncMaid.Core.Filtering;

namespace SyncMaid.Core.Model;

/// <summary>
/// A sync target: where filtered source files go, which files are selected, and
/// how the destination is reconciled. Immutable — edit with <c>with</c> expressions.
/// </summary>
public sealed record Destination(
    string Name,
    string Path,
    IReadOnlyList<FilterRule> Filters,
    SyncStrategy Strategy)
{
    /// <summary>
    /// Stable identity, generated once and preserved across edits (via <c>with</c>) so
    /// the destination's sync status survives renames. Persisted with the task.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// When true, every copied file is read back and its xxHash compared to the source
    /// before it is committed — guarding against silent hardware/environmental corruption
    /// the basic length check can't see. Off by default; on a mounted network path this
    /// re-reads each file over the network, so the editor warns when enabling it there.
    /// </summary>
    public bool VerifyContents { get; init; }

    /// <summary>How Mirror removes files no longer in the source. Defaults to the safe
    /// Recycle Bin so deletions are recoverable.</summary>
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Recycle;

    /// <summary>
    /// Fraction (0–1) of the destination that a single Mirror run may delete before it is
    /// aborted as a likely mistake (see <see cref="SyncMaid.Core.Sync.MirrorGuard"/>). Defaults to 0.5;
    /// set 0 to disable the ratio guard (the empty-source guard always applies).
    /// </summary>
    public double MassDeleteThreshold { get; init; } = 0.5;

    /// <summary>
    /// True when the source file at <paramref name="relativePath"/> should be synced
    /// to this destination. A file is included when it matches any rule; with no rules,
    /// nothing is selected. Rules are evaluated in order to keep behavior predictable
    /// once exclude-style rules are added.
    /// </summary>
    public bool Includes(string relativePath)
    {
        foreach (var rule in Filters)
        {
            if (rule.Matches(relativePath))
            {
                return true;
            }
        }

        return false;
    }
}
