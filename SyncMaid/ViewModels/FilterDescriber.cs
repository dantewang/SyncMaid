using System.Collections.Generic;
using System.Linq;
using SyncMaid.Core.Filtering;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Renders a <see cref="FilterRule"/> expression as compact plain text — e.g.
/// <c>docs/ and (jpg or png)</c> — for the editor's live preview and the destination-row
/// badge. Lives in the app layer to keep display strings out of the domain model.
/// The connective resources carry their own surrounding spacing (" and " in English,
/// none in CJK), so joining concatenates them verbatim.
/// </summary>
public static class FilterDescriber
{
    public static string Describe(FilterRule rule) => Describe(rule, nested: false);

    /// <summary>Describes one rule using the labelled wording shared by rule rows.</summary>
    public static string DescribeRow(FilterRule rule) => rule switch
    {
        AllFilesFilter => Strings.Filter_AllFiles,
        PathFilter path => Localizer.Format(Strings.Filter_PathRowFormat, path.Prefix),
        ExtensionFilter extension => Localizer.Format(Strings.Filter_ExtensionRowFormat, extension.Extension),
        // Composite expression from hand-edited JSON: use its compact plain-text form.
        _ => Describe(rule),
    };

    /// <summary>Describes a persisted filter list (OR semantics across elements).</summary>
    public static string Describe(IReadOnlyList<FilterRule> filters) => filters switch
    {
        [] => Strings.Filter_Nothing,
        [var single] => Describe(single),
        _ => Join(filters, Strings.Filter_Or, nested: false),
    };

    private static string Describe(FilterRule rule, bool nested) => rule switch
    {
        AllFilesFilter => Strings.Filter_AllFilesInline,
        PathFilter path => path.Prefix.TrimEnd('/', '\\') + "/",
        ExtensionFilter extension => extension.Extension.TrimStart('.'),
        NotFilter not => Strings.Filter_Not + Describe(not.Rule, nested: true),
        AllOfFilter allOf => Join(allOf.Rules, Strings.Filter_And, nested),
        AnyOfFilter anyOf => Join(anyOf.Rules, Strings.Filter_Or, nested),
        _ => rule.ToString() ?? string.Empty,
    };

    private static string Join(IReadOnlyList<FilterRule> rules, string connective, bool nested)
    {
        if (rules.Count == 0)
        {
            return Strings.Filter_Nothing;
        }

        var joined = string.Join(connective, rules.Select(rule => Describe(rule, nested: true)));
        return nested && rules.Count > 1 ? $"({joined})" : joined;
    }
}
