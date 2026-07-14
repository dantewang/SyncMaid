using CommunityToolkit.Mvvm.ComponentModel;
using SyncMaid.Core.Filtering;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// One rule row in a filter group: a domain <see cref="FilterRule"/> plus the row's exclude
/// toggle (persisted as a <see cref="NotFilter"/> wrapper). The rule may be a leaf the editor
/// created, or — from hand-edited JSON nested deeper than the two-level editor can represent —
/// a composite, which renders as a read-only summary and is persisted back verbatim.
/// </summary>
public sealed partial class FilterRuleViewModel : ViewModelBase
{
    /// <summary>When on, the rule selects everything <b>except</b> its matches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private bool _isExcluded;

    public FilterRuleViewModel(FilterRule rule, bool isExcluded = false)
    {
        Rule = rule;
        _isExcluded = isExcluded;
    }

    /// <summary>The wrapped rule, without the exclusion applied.</summary>
    public FilterRule Rule { get; }

    /// <summary>The rule as persisted: wrapped in a <see cref="NotFilter"/> when excluded.</summary>
    public FilterRule Lowered => IsExcluded ? new NotFilter(Rule) : Rule;

    public string Description
    {
        get
        {
            var body = FilterDescriber.DescribeRow(Rule);
            return IsExcluded ? Localizer.Format(Strings.Filter_ExcludeRowFormat, body) : body;
        }
    }
}
