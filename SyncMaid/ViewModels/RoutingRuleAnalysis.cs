using System;
using System.Collections.Generic;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;

namespace SyncMaid.ViewModels;

/// <summary>
/// Reads one routing rule against an earlier one to answer a single question: can the later
/// rule ever match anything? Under first-match-wins, overlapping rules are normal and
/// wanted — the mistake worth catching is the overlap that <em>hides</em> a rule completely.
/// </summary>
/// <remarks>
/// Deliberately partial. Deciding disjointness over the whole filter algebra is possible but
/// reports "overlap" for pairs that never collide in a real tree (<c>*.pdf</c> versus
/// <c>invoices/</c> being the routine routing case), which under first-match-wins is not a
/// problem at all. So this proves subsumption only for the shapes where it is unarguable —
/// an all-files rule, a shorter extension, an ancestor path — and stays quiet otherwise. A
/// silent verdict means "no proof", never "no overlap".
/// </remarks>
public static class RoutingRuleAnalysis
{
    /// <summary>
    /// True when <paramref name="earlier"/> provably takes every file
    /// <paramref name="later"/> would, so <paramref name="later"/> can never match.
    /// </summary>
    public static bool Subsumes(Destination earlier, Destination later)
    {
        if (earlier.Filters is [AllFilesFilter])
        {
            return true;
        }

        // A rule with no filters selects nothing, so it is unreachable for its own reasons —
        // not something an earlier rule did to it.
        if (later.Filters.Count == 0 || !TryLeaves(earlier, out var covering) || !TryLeaves(later, out var covered))
        {
            return false;
        }

        foreach (var rule in covered)
        {
            if (!covering.Exists(candidate => Covers(candidate, rule)))
            {
                return false;
            }
        }

        return true;
    }

    // Only the shape the editor writes for a simple rule: a flat OR list of path/extension
    // leaves. Anything with AND/OR/NOT structure is left alone rather than half-understood.
    private static bool TryLeaves(Destination destination, out List<FilterRule> leaves)
    {
        leaves = [];
        foreach (var filter in destination.Filters)
        {
            if (filter is not (PathFilter or ExtensionFilter))
            {
                return false;
            }

            leaves.Add(filter);
        }

        return leaves.Count > 0;
    }

    private static bool Covers(FilterRule earlier, FilterRule later) => (earlier, later) switch
    {
        // "gz" also matches "archive.tar.gz", so the shorter extension covers the longer one
        // — the comparison is on the suffix including the dot, so "gz" does not cover "targz".
        (ExtensionFilter first, ExtensionFilter second) =>
            ('.' + second.Extension).EndsWith('.' + first.Extension, StringComparison.OrdinalIgnoreCase),

        // An ancestor folder covers everything under it, itself included.
        (PathFilter first, PathFilter second) =>
            second.Prefix.Equals(first.Prefix, StringComparison.OrdinalIgnoreCase)
            || second.Prefix.StartsWith(first.Prefix + '/', StringComparison.OrdinalIgnoreCase),

        // A folder does not cover a file type, nor the other way round.
        _ => false,
    };
}
