using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public class TaskEditorViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private TaskTriggerType _selectedTriggerType = TaskTriggerType.Manual;
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

        var canOK = this.WhenAnyValue(
            x => x.Name,
            x => x.Path,
            x => x.SelectedTriggerType,
            x => x.CronExpression,
            (name, path, trigger, cron) =>
                !string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(path) &&
                (trigger != TaskTriggerType.Scheduled || !string.IsNullOrWhiteSpace(cron)));

        OKCommand = ReactiveCommand.Create(OK, canOK);
        CancelCommand = ReactiveCommand.Create(Cancel);
        BrowseCommand = ReactiveCommand.CreateFromTask(Browse);

        TriggerTypes = Enum.GetValues<TaskTriggerType>();
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

    public TaskTriggerType[] TriggerTypes { get; }

    public TaskTriggerType SelectedTriggerType
    {
        get => _selectedTriggerType;
        set => this.RaiseAndSetIfChanged(ref _selectedTriggerType, value);
    }

    public string CronExpression
    {
        get => _cronExpression;
        set => this.RaiseAndSetIfChanged(ref _cronExpression, value);
    }

    public bool IsScheduledTrigger => SelectedTriggerType == TaskTriggerType.Scheduled;

    public ReactiveCommand<Unit, Unit> OKCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }

    private void OK()
    {
        var task = new TaskModel(
            Name,
            Path,
            SelectedTriggerType,
            IsScheduledTrigger ? CronExpression : null);

        if (_hostWindow != null)
        {
            _hostWindow.Close(task);
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
                Title = "Select Source Folder",
                AllowMultiple = false
            });

            if (dialog.Count > 0)
            {
                Path = dialog[0].Path.LocalPath;
            }
        }
    }

    public void SetHostWindow(Window window)
    {
        _hostWindow = window;
    }
}
