using SyncMaid.Core.Filtering;

namespace SyncMaid.ViewModels;

/// <summary>
/// Wraps a domain <see cref="FilterRule"/> with a human-readable description for the
/// destination editor's filter list. Keeps display strings out of the domain model.
/// </summary>
public sealed class FilterRuleViewModel
{
    public FilterRuleViewModel(FilterRule rule) => Rule = rule;

    public FilterRule Rule { get; }

    public string Description => Rule switch
    {
        AllFilesFilter => "All files",
        PathFilter path => $"Path: {path.Prefix}",
        ExtensionFilter extension => $"Extension: {extension.Extension}",
        _ => Rule.ToString() ?? string.Empty,
    };
}
