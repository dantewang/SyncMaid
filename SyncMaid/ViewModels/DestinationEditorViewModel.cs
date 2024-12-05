using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public class DestinationEditorViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private bool _syncAll = true;
    private SyncStrategy _selectedStrategy = SyncStrategy.Sync;
    private string _newFilterPattern = string.Empty;
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

        var canOK = this.WhenAnyValue(
            x => x.Name,
            x => x.Path,
            x => x.SyncAll,
            x => x.Filters.Count,
            (name, path, syncAll, filterCount) =>
                !string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(path) &&
                (syncAll || filterCount > 0));

        var canAddFilter = this.WhenAnyValue(
            x => x.NewFilterPattern,
            pattern => !string.IsNullOrWhiteSpace(pattern));

        OKCommand = ReactiveCommand.Create(OK, canOK);
        CancelCommand = ReactiveCommand.Create(Cancel);
        BrowseCommand = ReactiveCommand.CreateFromTask(Browse);
        AddFilterCommand = ReactiveCommand.Create(AddFilter, canAddFilter);
        RemoveFilterCommand = ReactiveCommand.Create<FilterRule>(RemoveFilter);

        SyncStrategies = Enum.GetValues<SyncStrategy>();
        FilterTypes = Enum.GetValues<FilterType>();
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set => this.RaiseAndSetIfChanged(ref _path, value);
    }

    public bool SyncAll
    {
        get => _syncAll;
        set => this.RaiseAndSetIfChanged(ref _syncAll, value);
    }

    public SyncStrategy[] SyncStrategies { get; }
    public FilterType[] FilterTypes { get; }

    public SyncStrategy SelectedStrategy
    {
        get => _selectedStrategy;
        set => this.RaiseAndSetIfChanged(ref _selectedStrategy, value);
    }

    public string NewFilterPattern
    {
        get => _newFilterPattern;
        set => this.RaiseAndSetIfChanged(ref _newFilterPattern, value);
    }

    public FilterType SelectedFilterType
    {
        get => _selectedFilterType;
        set => this.RaiseAndSetIfChanged(ref _selectedFilterType, value);
    }

    public ObservableCollection<FilterRule> Filters { get; }

    public ReactiveCommand<Unit, Unit> OKCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddFilterCommand { get; }
    public ReactiveCommand<FilterRule, Unit> RemoveFilterCommand { get; }

    private void OK()
    {
        var destination = new DestinationModel(
            Name,
            Path,
            SyncAll,
            SelectedStrategy,
            Filters);

        if (_hostWindow != null)
        {
            _hostWindow.Close(destination);
        }
    }

    private void Cancel()
    {
        if (_hostWindow != null)
        {
            _hostWindow.Close();
        }
    }

    private async Task Browse()
    {
        if (_hostWindow != null)
        {
            var dialog = await _hostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Destination Folder",
                AllowMultiple = false
            });

            if (dialog.Count > 0)
            {
                Path = dialog[0].Path.LocalPath;
            }
        }
    }

    private void AddFilter()
    {
        var filter = new FilterRule(NewFilterPattern, SelectedFilterType);
        Filters.Add(filter);
        NewFilterPattern = string.Empty;
    }

    private void RemoveFilter(FilterRule filter)
    {
        Filters.Remove(filter);
    }

    public void SetHostWindow(Window window)
    {
        _hostWindow = window;
    }
}
