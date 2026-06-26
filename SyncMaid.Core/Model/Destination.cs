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
