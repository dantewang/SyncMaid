using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Edits a single destination: its path, sync strategy, and filter rules. "Sync all" maps to a
/// single <see cref="AllFilesFilter"/>; otherwise the user builds rule <b>groups</b> — each
/// group joins its rules with its own ANY/ALL connective, and the groups combine with a
/// top-level ANY/ALL — a two-level editor covering <c>(A or B) and C</c> and
/// <c>A or (B and C)</c>. Trivial shapes collapse on save so simple configs persist exactly
/// as before (a flat OR list). Raises <see cref="CloseRequested"/> instead of touching the
/// window.
/// </summary>
public partial class DestinationEditorViewModel : EditorDialogViewModel<Destination>
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private bool _syncAll = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilterEditor))]
    [NotifyPropertyChangedFor(nameof(ShowMoveOptions))]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private SyncStrategy _selectedStrategy = SyncStrategy.Mirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVerifyNetworkWarning))]
    private bool _verifyContents;

    [ObservableProperty]
    private DeleteMode _selectedDeleteMode = DeleteMode.Recycle;

    /// <summary>Move only: land files in the destination root instead of keeping the folders
    /// they sat in. See <see cref="Destination.FlattenStructure"/>.</summary>
    [ObservableProperty]
    private bool _flattenStructure;

    /// <summary>Move only: what a flattened file does when its name is already taken.</summary>
    [ObservableProperty]
    private FileNameCollisionPolicy _selectedCollisionPolicy = FileNameCollisionPolicy.Skip;

    /// <summary>Whether the mass-delete guard is on (off = never ask, threshold 0).</summary>
    [ObservableProperty]
    private bool _confirmLargeDeletions = true;

    /// <summary>The guard threshold as a whole percentage (persisted as a 0–1 fraction).</summary>
    [ObservableProperty]
    private decimal _massDeletePercent = 50;

    /// <summary>Top-level connective: false = a file may match ANY group, true = must match ALL.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterPreview))]
    private bool _matchAllGroups;

    private readonly string _sourcePath;

    /// <param name="directoryExists">Directory probe, injectable for tests;
    /// defaults to <see cref="System.IO.Directory.Exists"/> (never throws — returns false
    /// for invalid/partial input, so it is safe to call while the user types).</param>
    private readonly Func<string, DestinationConflict?>? _destinationConflicts;

    public DestinationEditorViewModel(
        IFolderPickerService folderPicker,
        Destination? existing = null,
        string sourcePath = "",
        Func<string, bool>? directoryExists = null,
        SyncTaskKind taskKind = SyncTaskKind.Sync,
        Func<string, DestinationConflict?>? destinationConflicts = null)
        : base(
            folderPicker,
            Strings.Dialog_SelectDestinationFolder,
            existing?.Id,
            existing?.Name,
            existing?.LocalPath,
            directoryExists)
    {
        _sourcePath = sourcePath;
        _destinationConflicts = destinationConflicts;
        // The task's kind decides the strategy: a Move task routes, so its destinations are
        // Move and there is nothing to choose; a Sync task copies, so Move is not on offer.
        IsRoutingRule = taskKind == SyncTaskKind.Move;
        _selectedStrategy = IsRoutingRule ? SyncStrategy.Move : SyncStrategy.Mirror;
        SyncStrategies = Enum.GetValues<SyncStrategy>();
        DeleteModes = Enum.GetValues<DeleteMode>();
        Groups = new ObservableCollection<FilterGroupViewModel>();
        Groups.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMultipleGroups));
            OnFiltersChanged();
        };

        if (existing != null)
        {
            _selectedStrategy = existing.Strategy;
            _verifyContents = existing.VerifyContents;
            _selectedDeleteMode = existing.DeleteMode;
            _flattenStructure = existing.FlattenStructure;
            _selectedCollisionPolicy = existing.CollisionPolicy;

            // 0 (or less) means the guard is off; otherwise show it as a whole percentage.
            _confirmLargeDeletions = existing.MassDeleteThreshold > 0;
            if (_confirmLargeDeletions)
            {
                _massDeletePercent = (decimal)Math.Round(
                    Math.Clamp(existing.MassDeleteThreshold * 100, 1, 100));
            }

            // A lone AllFilesFilter is "sync all"; anything else raises into the group editor.
            var isSyncAll = existing.Filters is [AllFilesFilter];
            _syncAll = isSyncAll;
            if (!isSyncAll)
            {
                _matchAllGroups = LoadFilters(existing.Filters);
            }
        }
        if (Groups.Count == 0)
        {
            AddGroup(); // the editor always shows at least one (possibly empty) group
        }
    }

    public SyncStrategy[] SyncStrategies { get; }
    public DeleteMode[] DeleteModes { get; }

    /// <summary>True when this destination belongs to a Move task: it is one rule of an
    /// ordered routing list, so its strategy is settled and the editor shows Move's own
    /// options instead of a strategy choice.</summary>
    public bool IsRoutingRule { get; }

    /// <summary>Dialog heading — a routing rule is a different thing from a sync destination.</summary>
    public string Title => IsRoutingRule ? Strings.DestEditor_RoutingRuleTitle : Strings.DestEditor_Title;

    /// <summary>Whether the strategy radio group is shown; a Move task has no choice to make.</summary>
    public bool ShowStrategyChoice => !IsRoutingRule;

    /// <summary>Whether Move's own options (flatten, collision policy) are shown.</summary>
    public bool ShowMoveOptions => SelectedStrategy == SyncStrategy.Move;

    /// <summary>
    /// False for Mirror, hiding the whole "files to sync" section. Mirror's contract is
    /// tree identity — the destination replicates the whole source tree — so file filters
    /// are a conceptual conflict there. Group state is kept, so switching the strategy
    /// back restores whatever was built; saving as Mirror persists a lone
    /// <see cref="AllFilesFilter"/> (normalizing legacy hand-edited config too).
    /// </summary>
    public bool ShowFilterEditor => SelectedStrategy != SyncStrategy.Mirror;

    /// <summary>The rule groups; each combines its own rules with its ANY/ALL connective.</summary>
    public ObservableCollection<FilterGroupViewModel> Groups { get; }

    /// <summary>True when the top-level connective and per-group remove buttons matter.</summary>
    public bool HasMultipleGroups => Groups.Count > 1;

    /// <summary>Live plain-text rendering of the whole expression, e.g.
    /// <c>docs/ and (jpg or png)</c> — the guard against building the wrong logic.</summary>
    public string FilterPreview
    {
        get
        {
            var filters = BuildFilters();
            return filters.Count == 0
                ? Strings.Filter_NoRulesPreview
                : Localizer.Format(Strings.Filter_SyncsPreviewFormat, FilterDescriber.Describe(filters));
        }
    }

    /// <summary>True when the destination path is a mounted network location (UNC or a
    /// mapped network drive), where content verification means re-reading over the network.</summary>
    public bool IsNetworkPath => NetworkPath.IsNetwork(Path);

    /// <summary>Whether to show the "verifying over the network is slow" caution.</summary>
    public bool ShowVerifyNetworkWarning => VerifyContents && IsNetworkPath;

    /// <summary>Explains a blocked nested path or destination overlap, or the non-blocking
    /// missing-folder hint.</summary>
    public string PathHintText => HasUnsafeNesting
        ? Strings.DestEditor_NestedPathHint
        : Conflict is { } conflict
            ? Localizer.Format(
                conflict.WithinTask
                    ? Strings.DestEditor_SiblingOverlapHintFormat
                    : Strings.DestEditor_DestinationOverlapHintFormat,
                conflict.Name)
            : Strings.DestEditor_MissingFolderHint;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK()
    {
        IReadOnlyList<FilterRule> filters = !ShowFilterEditor || SyncAll
            ? [new AllFilesFilter()]
            : BuildFilters();

        Close(new Destination(Name, Path, filters, SelectedStrategy)
        {
            Id = EditorId,
            VerifyContents = VerifyContents,
            DeleteMode = SelectedDeleteMode,
            MassDeleteThreshold = ConfirmLargeDeletions ? (double)Math.Clamp(MassDeletePercent, 1, 100) / 100.0 : 0,
            FlattenStructure = ShowMoveOptions && FlattenStructure,
            CollisionPolicy = SelectedCollisionPolicy,
        });
    }

    protected override IRelayCommand AcceptCommand => OKCommand;

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && !HasUnsafeNesting
        && Conflict is null
        && (!ShowFilterEditor || SyncAll || Groups.Any(group => group.Rules.Count > 0));

    // Task shape convention (AGENT.md): source and destinations never nest, in either
    // direction, for every strategy. The engine enforces the same rule at run start.
    private bool HasUnsafeNesting => RelativePaths.Overlaps(Path, _sourcePath);

    // Task shape convention (AGENT.md): destinations never overlap, whether they belong to
    // this task or another one. The probe is supplied by the owning task node, which knows
    // both its own siblings and (via the task list) every other task's destinations.
    private DestinationConflict? Conflict =>
        string.IsNullOrWhiteSpace(Path) ? null : _destinationConflicts?.Invoke(Path);

    protected override bool HasAdditionalPathWarning =>
        HasUnsafeNesting || Conflict is not null;

    protected override void OnEditorPathChanged()
    {
        OnPropertyChanged(nameof(IsNetworkPath));
        OnPropertyChanged(nameof(ShowVerifyNetworkWarning));
        OnPropertyChanged(nameof(PathHintText));
    }

    [RelayCommand]
    private void AddGroup() => Groups.Add(new FilterGroupViewModel(OnFiltersChanged));

    [RelayCommand]
    private void RemoveGroup(FilterGroupViewModel group)
    {
        Groups.Remove(group);
        if (Groups.Count == 0)
        {
            AddGroup(); // never leave the panel without an add-rule input
        }
    }

    private void OnFiltersChanged()
    {
        OnPropertyChanged(nameof(FilterPreview));
        OKCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Lowers the group tree to the persisted <see cref="Destination.Filters"/> list (whose
    /// elements OR together). Empty groups are skipped; a single ANY group flattens to the
    /// plain rule list (today's format); a top-level ALL becomes one <see cref="AllOfFilter"/>
    /// element.
    /// </summary>
    private IReadOnlyList<FilterRule> BuildFilters()
    {
        var groups = Groups
            .Select(group => group.Lower())
            .Where(rule => rule is not null)
            .Select(rule => rule!)
            .ToList();

        return groups switch
        {
            [] => [],
            [AnyOfFilter anyOf] => anyOf.Rules,   // single ANY group → flat OR list, as before
            [var single] => [single],
            _ => MatchAllGroups ? [new AllOfFilter(groups)] : groups,
        };
    }

    /// <summary>Raises a persisted filter list back into the two-level group editor.
    /// Returns the top-level connective (true = match ALL groups).</summary>
    private bool LoadFilters(IReadOnlyList<FilterRule> filters)
    {
        if (filters is [AllOfFilter allOf])
        {
            // Top-level ALL: each conjunct is a group.
            foreach (var element in allOf.Rules)
            {
                Groups.Add(RaiseGroup(element));
            }

            return true;
        }

        if (filters.Any(filter => filter is AllOfFilter or AnyOfFilter))
        {
            // An OR list containing composites: each element is a group.
            foreach (var element in filters)
            {
                Groups.Add(RaiseGroup(element));
            }
        }
        else
        {
            // A flat list of leaves — today's simple shape: one ANY group holding them all.
            var group = new FilterGroupViewModel(OnFiltersChanged);
            foreach (var rule in filters)
            {
                group.AddRaised(rule);
            }

            Groups.Add(group);
        }

        return false;
    }

    // One element of the top level → one group card. Anything nested deeper than the editor's
    // two levels stays intact inside a summary row (AddRaised persists it back verbatim).
    private FilterGroupViewModel RaiseGroup(FilterRule rule)
    {
        var group = new FilterGroupViewModel(OnFiltersChanged);
        switch (rule)
        {
            case AnyOfFilter anyOf:
                foreach (var child in anyOf.Rules)
                {
                    group.AddRaised(child);
                }

                break;

            case AllOfFilter allOf:
                group.MatchAll = true;
                foreach (var child in allOf.Rules)
                {
                    group.AddRaised(child);
                }

                break;

            default:
                group.AddRaised(rule);
                break;
        }

        return group;
    }
}
