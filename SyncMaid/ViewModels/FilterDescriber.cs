using System.Collections.Generic;
using System.Linq;
using SyncMaid.Core.Filtering;

namespace SyncMaid.ViewModels;

/// <summary>
/// Renders a <see cref="FilterRule"/> expression as compact plain text — e.g.
/// <c>docs/ and (jpg or png)</c> — for the editor's live preview and the destination-row
/// badge. Lives in the app layer to keep display strings out of the domain model.
/// </summary>
public static class FilterDescriber
{
    public static string Describe(FilterRule rule) => Describe(rule, nested: false);

    /// <summary>Describes one rule using the labelled wording shared by rule rows.</summary>
    public static string DescribeRow(FilterRule rule) => rule switch
    {
        AllFilesFilter => "All files",
        PathFilter path => $"Path: {path.Prefix}",
        ExtensionFilter extension => $"Extension: {extension.Extension}",
        // Composite expression from hand-edited JSON: use its compact plain-text form.
        _ => Describe(rule),
    };

    /// <summary>Describes a persisted filter list (OR semantics across elements).</summary>
    public static string Describe(IReadOnlyList<FilterRule> filters) => filters switch
    {
        [] => "nothing",
        [var single] => Describe(single),
        _ => Join(filters, " or ", nested: false),
    };

    private static string Describe(FilterRule rule, bool nested) => rule switch
    {
        AllFilesFilter => "all files",
        PathFilter path => path.Prefix.TrimEnd('/', '\\') + "/",
        ExtensionFilter extension => extension.Extension.TrimStart('.'),
        NotFilter not => "not " + Describe(not.Rule, nested: true),
        AllOfFilter allOf => Join(allOf.Rules, " and ", nested),
        AnyOfFilter anyOf => Join(anyOf.Rules, " or ", nested),
        _ => rule.ToString() ?? string.Empty,
    };

    private static string Join(IReadOnlyList<FilterRule> rules, string connective, bool nested)
    {
        if (rules.Count == 0)
        {
            return "nothing";
        }

        var joined = string.Join(connective, rules.Select(rule => Describe(rule, nested: true)));
        return nested && rules.Count > 1 ? $"({joined})" : joined;
    }
}
