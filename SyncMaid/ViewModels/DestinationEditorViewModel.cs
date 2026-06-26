using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public partial class DestinationEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _path = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private bool _syncAll = true;

    [ObservableProperty]
    private SyncStrategy _selectedStrategy = SyncStrategy.Sync;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))]
    private string _newFilterPattern = string.Empty;

    [ObservableProperty]
    private FilterType _selectedFilterType = FilterType.Wildcard;

    private Window? _hostWindow;

    public DestinationEditorViewModel(DestinationModel? existingDestination = null)
    {
        if (existingDestination != null)
        {
            _name = existingDestination.Name;
            _path = existingDestination.Path;
            _syncAll = existingDestination.SyncAll;
            _selectedStrategy = existingDestination.Strategy;
            Filters = new ObservableCollection<FilterRule>(existingDestination.Filters);
        }
        else
        {
            Filters = new ObservableCollection<FilterRule>();
        }

        // OK validity depends on whether any filters exist when not syncing all.
        Filters.CollectionChanged += (_, _) => OKCommand.NotifyCanExecuteChanged();

        SyncStrategies = Enum.GetValues<SyncStrategy>();
        FilterTypes = Enum.GetValues<FilterType>();
    }

    public SyncStrategy[] SyncStrategies { get; }
    public FilterType[] FilterTypes { get; }
    public ObservableCollection<FilterRule> Filters { get; }

    public void SetHostWindow(Window window) => _hostWindow = window;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK()
    {
        var destination = new DestinationModel(Name, Path, SyncAll, SelectedStrategy, Filters);
        _hostWindow?.Close(destination);
    }

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && (SyncAll || Filters.Count > 0);

    [RelayCommand]
    private void Cancel() => _hostWindow?.Close();

    [RelayCommand]
    private async Task Browse()
    {
        if (_hostWindow == null) return;

        var folders = await _hostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Destination Folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            Path = folders[0].Path.LocalPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddFilter))]
    private void AddFilter()
    {
        Filters.Add(new FilterRule(NewFilterPattern, SelectedFilterType));
        NewFilterPattern = string.Empty;
    }

    private bool CanAddFilter() => !string.IsNullOrWhiteSpace(NewFilterPattern);

    [RelayCommand]
    private void RemoveFilter(FilterRule filter) => Filters.Remove(filter);
}
