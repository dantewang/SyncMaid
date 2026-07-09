using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
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
public partial class DestinationEditorViewModel : DialogViewModel<Destination>
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    [NotifyPropertyChangedFor(nameof(IsNetworkPath))]
    [NotifyPropertyChangedFor(nameof(ShowVerifyNetworkWarning))]
    [NotifyPropertyChangedFor(nameof(ShowPathHint))]
    private string _path = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private bool _syncAll = true;

    [ObservableProperty]
    private SyncStrategy _selectedStrategy = SyncStrategy.Mirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVerifyNetworkWarning))]
    private bool _verifyContents;

    [ObservableProperty]
    private DeleteMode _selectedDeleteMode = DeleteMode.Recycle;

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

    private readonly IFolderPickerService _folderPicker;
    private readonly Func<string, bool> _directoryExists;
    private readonly Guid _id;

    /// <param name="directoryExists">Directory probe, injectable for tests;
    /// defaults to <see cref="System.IO.Directory.Exists"/> (never throws — returns false
    /// for invalid/partial input, so it is safe to call while the user types).</param>
    public DestinationEditorViewModel(
        IFolderPickerService folderPicker,
        Destination? existing = null,
        Func<string, bool>? directoryExists = null)
    {
        _folderPicker = folderPicker;
        _directoryExists = directoryExists ?? System.IO.Directory.Exists;
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
            _id = existing.Id;   // preserve identity so status stays linked across edits
            _name = existing.Name;
            _path = existing.LocalPath;
            _selectedStrategy = existing.Strategy;
            _verifyContents = existing.VerifyContents;
            _selectedDeleteMode = existing.DeleteMode;

            // 0 (or less) means the guard is off; otherwise show it as a whole percentage.
            _confirmLargeDeletions = existing.MassDeleteThreshold > 0;
            if (_confirmLargeDeletions)
            {
                _massDeletePercent = Math.Clamp((decimal)Math.Round(existing.MassDeleteThreshold * 100), 1, 100);
            }

            // A lone AllFilesFilter is "sync all"; anything else raises into the group editor.
            var isSyncAll = existing.Filters is [AllFilesFilter];
            _syncAll = isSyncAll;
            if (!isSyncAll)
            {
                _matchAllGroups = LoadFilters(existing.Filters);
            }
        }
        else
        {
            _id = Guid.NewGuid();
        }

        if (Groups.Count == 0)
        {
            AddGroup(); // the editor always shows at least one (possibly empty) group
        }
    }

    public SyncStrategy[] SyncStrategies { get; }
    public DeleteMode[] DeleteModes { get; }

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
                ? "No rules yet — nothing will be synced."
                : "Syncs: " + FilterDescriber.Describe(filters);
        }
    }

    /// <summary>True when the destination path is a mounted network location (UNC or a
    /// mapped network drive), where content verification means re-reading over the network.</summary>
    public bool IsNetworkPath => NetworkPath.IsNetwork(Path);

    /// <summary>Whether to show the "verifying over the network is slow" caution.</summary>
    public bool ShowVerifyNetworkWarning => VerifyContents && IsNetworkPath;

    /// <summary>Non-blocking typo guard: the destination folder doesn't exist (yet). Saving is
    /// fine — the first run creates it — but a typo would silently sync somewhere unintended.</summary>
    public bool ShowPathHint => !string.IsNullOrWhiteSpace(Path) && !_directoryExists(Path);

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK()
    {
        IReadOnlyList<FilterRule> filters = SyncAll ? [new AllFilesFilter()] : BuildFilters();

        Close(new Destination(Name, Path, filters, SelectedStrategy)
        {
            Id = _id,
            VerifyContents = VerifyContents,
            DeleteMode = SelectedDeleteMode,
            MassDeleteThreshold = ConfirmLargeDeletions ? (double)Math.Clamp(MassDeletePercent, 1, 100) / 100.0 : 0,
        });
    }

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && (SyncAll || Groups.Any(group => group.Rules.Count > 0));

    [RelayCommand]
    private void Cancel() => Close(null);

    /// <summary>Enter saves when the form is valid.</summary>
    public override bool RequestAccept()
    {
        if (!OKCommand.CanExecute(null))
        {
            return false;
        }

        OKCommand.Execute(null);
        return true;
    }

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await _folderPicker.PickFolderAsync("Select Destination Folder");
        if (folder != null)
        {
            Path = folder;
        }
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
