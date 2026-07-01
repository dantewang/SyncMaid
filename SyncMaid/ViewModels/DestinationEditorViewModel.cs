using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Edits a single destination: its path, sync strategy, and filter rules. "Sync all"
/// maps to a single <see cref="AllFilesFilter"/>; otherwise the user builds a list of
/// path/extension rules. Raises <see cref="CloseRequested"/> instead of touching the
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))]
    private string _newFilterPattern = string.Empty;

    [ObservableProperty]
    private FilterKind _selectedFilterKind = FilterKind.Path;

    private readonly IFolderPickerService _folderPicker;
    private readonly Guid _id;

    public DestinationEditorViewModel(IFolderPickerService folderPicker, Destination? existing = null)
    {
        _folderPicker = folderPicker;
        SyncStrategies = Enum.GetValues<SyncStrategy>();
        FilterKinds = Enum.GetValues<FilterKind>();
        DeleteModes = Enum.GetValues<DeleteMode>();
        Filters = new ObservableCollection<FilterRuleViewModel>();

        if (existing != null)
        {
            _id = existing.Id;   // preserve identity so status stays linked across edits
            _name = existing.Name;
            _path = existing.LocalPath;
            _selectedStrategy = existing.Strategy;
            _verifyContents = existing.VerifyContents;
            _selectedDeleteMode = existing.DeleteMode;

            // A lone AllFilesFilter is "sync all"; anything else is an explicit filter list.
            var isSyncAll = existing.Filters is [AllFilesFilter];
            _syncAll = isSyncAll;
            if (!isSyncAll)
            {
                foreach (var rule in existing.Filters)
                {
                    Filters.Add(new FilterRuleViewModel(rule));
                }
            }
        }
        else
        {
            _id = Guid.NewGuid();
        }

        // OK validity depends on having at least one filter when not syncing all.
        Filters.CollectionChanged += (_, _) => OKCommand.NotifyCanExecuteChanged();
    }

    public SyncStrategy[] SyncStrategies { get; }
    public FilterKind[] FilterKinds { get; }
    public DeleteMode[] DeleteModes { get; }
    public ObservableCollection<FilterRuleViewModel> Filters { get; }

    /// <summary>True when the destination path is a mounted network location (UNC or a
    /// mapped network drive), where content verification means re-reading over the network.</summary>
    public bool IsNetworkPath => LooksLikeNetworkPath(Path);

    /// <summary>Whether to show the "verifying over the network is slow" caution.</summary>
    public bool ShowVerifyNetworkWarning => VerifyContents && IsNetworkPath;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK()
    {
        IReadOnlyList<FilterRule> filters = SyncAll
            ? [new AllFilesFilter()]
            : Filters.Select(filter => filter.Rule).ToList();

        Close(new Destination(Name, Path, filters, SelectedStrategy)
        {
            Id = _id,
            VerifyContents = VerifyContents,
            DeleteMode = SelectedDeleteMode,
        });
    }

    // UNC paths and mapped network drives mean content verification re-reads over the
    // network. Computed in the VM so the editor can warn; tolerant of partial input.
    private static bool LooksLikeNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            return !string.IsNullOrEmpty(root)
                   && new System.IO.DriveInfo(root).DriveType == System.IO.DriveType.Network;
        }
        catch
        {
            return false; // mid-typing or invalid path — not yet a known network path
        }
    }

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && (SyncAll || Filters.Count > 0);

    [RelayCommand]
    private void Cancel() => Close(null);

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await _folderPicker.PickFolderAsync("Select Destination Folder");
        if (folder != null)
        {
            Path = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddFilter))]
    private void AddFilter()
    {
        FilterRule rule = SelectedFilterKind switch
        {
            FilterKind.Extension => new ExtensionFilter(NewFilterPattern),
            _ => new PathFilter(NewFilterPattern),
        };

        Filters.Add(new FilterRuleViewModel(rule));
        NewFilterPattern = string.Empty;
    }

    private bool CanAddFilter() => !string.IsNullOrWhiteSpace(NewFilterPattern);

    [RelayCommand]
    private void RemoveFilter(FilterRuleViewModel filter) => Filters.Remove(filter);
}
