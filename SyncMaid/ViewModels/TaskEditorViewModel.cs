using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public partial class TaskEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _path = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    [NotifyPropertyChangedFor(nameof(IsScheduledTrigger))]
    private TaskTriggerType _selectedTriggerType = TaskTriggerType.Manual;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    private string _cronExpression = string.Empty;

    private Window? _hostWindow;

    public TaskEditorViewModel(TaskModel? existingTask = null)
    {
        if (existingTask != null)
        {
            _name = existingTask.Name;
            _path = existingTask.Path;
            _selectedTriggerType = existingTask.TriggerType;
            _cronExpression = existingTask.CronExpression ?? string.Empty;
        }

        TriggerTypes = Enum.GetValues<TaskTriggerType>();
    }

    public TaskTriggerType[] TriggerTypes { get; }

    public bool IsScheduledTrigger => SelectedTriggerType == TaskTriggerType.Scheduled;

    public void SetHostWindow(Window window) => _hostWindow = window;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK()
    {
        var task = new TaskModel(
            Name,
            Path,
            SelectedTriggerType,
            IsScheduledTrigger ? CronExpression : null);

        _hostWindow?.Close(task);
    }

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && (SelectedTriggerType != TaskTriggerType.Scheduled || !string.IsNullOrWhiteSpace(CronExpression));

    [RelayCommand]
    private void Cancel() => _hostWindow?.Close();

    [RelayCommand]
    private async Task Browse()
    {
        if (_hostWindow == null) return;

        var folders = await _hostWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Source Folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            Path = folders[0].Path.LocalPath;
        }
    }
}
