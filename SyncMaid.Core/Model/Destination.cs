using System.Text.Json.Serialization;
using SyncMaid.Core.Filtering;

namespace SyncMaid.Core.Model;

/// <summary>
/// A sync target: where filtered source files go (its <see cref="Target"/> location),
/// which files are selected, and how the destination is reconciled. Immutable — edit with
/// <c>with</c> expressions.
/// </summary>
public sealed record Destination
{
    /// <summary>Canonical constructor. Marked for the serializer since the string-path
    /// overload gives <see cref="Destination"/> two constructors.</summary>
    [JsonConstructor]
    public Destination(
        string name,
        DestinationLocation target,
        IReadOnlyList<FilterRule> filters,
        SyncStrategy strategy)
    {
        Name = name;
        Target = target;
        Filters = filters;
        Strategy = strategy;
    }

    /// <summary>Convenience overload for the common local/mounted case — wraps
    /// <paramref name="path"/> in a <see cref="LocalDestination"/>.</summary>
    public Destination(string name, string path, IReadOnlyList<FilterRule> filters, SyncStrategy strategy)
        : this(name, new LocalDestination(path), filters, strategy)
    {
    }

    /// <summary>Display name.</summary>
    public string Name { get; init; }

    /// <summary>Where this destination's files live (local/mounted today; cloud/SFTP later).</summary>
    public DestinationLocation Target { get; init; }

    /// <summary>The filter rules selecting which source files sync here.</summary>
    public IReadOnlyList<FilterRule> Filters { get; init; }

    /// <summary>How the destination is reconciled with the filtered source.</summary>
    public SyncStrategy Strategy { get; init; }

    /// <summary>
    /// Stable identity, generated once and preserved across edits (via <c>with</c>) so
    /// the destination's sync status survives renames. Persisted with the task.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The destination path when <see cref="Target"/> is a local/mounted location;
    /// empty otherwise. For display and the editor, which are path-based in phase 1.</summary>
    [JsonIgnore]
    public string LocalPath => (Target as LocalDestination)?.Path ?? string.Empty;

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
