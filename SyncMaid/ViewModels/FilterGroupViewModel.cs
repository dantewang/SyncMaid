using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;

namespace SyncMaid.ViewModels;

/// <summary>
/// One group card in the destination editor's filter panel: a set of rule rows joined by the
/// group's own ANY/ALL connective, with its own add-rule input. Groups combine at the top
/// level via the editor's ANY/ALL — together covering <c>(A or B) and C</c> and
/// <c>A or (B and C)</c> without a free-form expression editor.
/// </summary>
public sealed partial class FilterGroupViewModel : ViewModelBase
{
    private readonly Action _changed;

    /// <summary>The group's connective: false = match ANY rule (OR), true = match ALL (AND).</summary>
    [ObservableProperty]
    private bool _matchAll;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuleCommand))]
    private string _newFilterPattern = string.Empty;

    [ObservableProperty]
    private FilterKind _selectedFilterKind = FilterKind.Path;

    /// <param name="changed">Raised on any change that affects the lowered expression, so the
    /// editor can refresh its preview and OK validity.</param>
    public FilterGroupViewModel(Action changed)
    {
        _changed = changed;
        Rules = new ObservableCollection<FilterRuleViewModel>();
        Rules.CollectionChanged += (_, _) => _changed();
    }

    public ObservableCollection<FilterRuleViewModel> Rules { get; }

    public FilterKind[] FilterKinds { get; } = Enum.GetValues<FilterKind>();

    partial void OnMatchAllChanged(bool value) => _changed();

    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private void AddRule()
    {
        FilterRule rule = SelectedFilterKind switch
        {
            FilterKind.Extension => new ExtensionFilter(NewFilterPattern),
            _ => new PathFilter(NewFilterPattern),
        };

        Add(new FilterRuleViewModel(rule));
        NewFilterPattern = string.Empty;
    }

    private bool CanAddRule() => !string.IsNullOrWhiteSpace(NewFilterPattern);

    [RelayCommand]
    private void RemoveRule(FilterRuleViewModel rule) => Rules.Remove(rule);

    /// <summary>Adds a row and wires its exclude toggle into the change notification.</summary>
    public void Add(FilterRuleViewModel rule)
    {
        rule.PropertyChanged += (_, _) => _changed();
        Rules.Add(rule);
    }

    /// <summary>Adds a persisted rule as a row, unwrapping a <see cref="NotFilter"/> into the
    /// row's exclude toggle. A deeper composite stays intact as a summary row.</summary>
    public void AddRaised(FilterRule rule)
    {
        if (rule is NotFilter not)
        {
            Add(new FilterRuleViewModel(not.Rule, isExcluded: true));
        }
        else
        {
            Add(new FilterRuleViewModel(rule));
        }
    }

    /// <summary>
    /// Lowers the group to its persisted form: null when empty (skipped), the bare rule when
    /// there is exactly one, otherwise an <see cref="AllOfFilter"/>/<see cref="AnyOfFilter"/>
    /// per the connective — so simple configs persist exactly as before.
    /// </summary>
    public FilterRule? Lower()
    {
        var rules = Rules.Select(rule => rule.Lowered).ToList();
        return rules.Count switch
        {
            0 => null,
            1 => rules[0],
            _ => MatchAll ? new AllOfFilter(rules) : new AnyOfFilter(rules),
        };
    }
}
