using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Edits a task's own fields (name, source, trigger). Destinations are managed by the
/// task node, not here. Raises <see cref="CloseRequested"/> with the result (or null on
/// cancel) instead of touching the window, so it stays view-free and testable.
/// </summary>
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
    [NotifyPropertyChangedFor(nameof(CronPreview))]
    private string _cronExpression = string.Empty;

    private readonly IFolderPickerService _folderPicker;
    private readonly Guid _id;

    public TaskEditorViewModel(IFolderPickerService folderPicker, SyncTask? existing = null)
    {
        _folderPicker = folderPicker;
        TriggerTypes = Enum.GetValues<TaskTriggerType>();

        if (existing != null)
        {
            _id = existing.Id;   // preserve identity so status stays linked across edits
            _name = existing.Name;
            _path = existing.SourcePath;
            (_selectedTriggerType, _cronExpression) = FromTrigger(existing.Trigger);
        }
        else
        {
            _id = Guid.NewGuid();
        }
    }

    /// <summary>Raised when the dialog should close: the new task, or null if cancelled.</summary>
    public event Action<SyncTask?>? CloseRequested;

    public TaskTriggerType[] TriggerTypes { get; }

    public bool IsScheduledTrigger => SelectedTriggerType == TaskTriggerType.Scheduled;

    /// <summary>Plain-language feedback for the cron field: validity and the next run time.</summary>
    public string CronPreview
    {
        get
        {
            if (!CronSchedule.IsValid(CronExpression))
            {
                return "Enter a valid cron expression (e.g. */5 * * * *)";
            }

            var next = CronSchedule.NextOccurrenceUtc(CronExpression, DateTime.UtcNow);
            return next is null
                ? "Valid, but has no upcoming runs"
                : $"Next run: {next.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK() =>
        // Destinations are merged in by the caller; a fresh task carries none.
        CloseRequested?.Invoke(new SyncTask(Name, Path, ToTrigger(), []) { Id = _id });

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && (SelectedTriggerType != TaskTriggerType.Scheduled || CronSchedule.IsValid(CronExpression));

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await _folderPicker.PickFolderAsync("Select Source Folder");
        if (folder != null)
        {
            Path = folder;
        }
    }

    private Trigger ToTrigger() => SelectedTriggerType switch
    {
        TaskTriggerType.Scheduled => new ScheduledTrigger(CronExpression),
        TaskTriggerType.Watch => new WatchTrigger(),
        _ => new ManualTrigger(),
    };

    private static (TaskTriggerType Type, string Cron) FromTrigger(Trigger trigger) => trigger switch
    {
        ScheduledTrigger scheduled => (TaskTriggerType.Scheduled, scheduled.CronExpression),
        WatchTrigger => (TaskTriggerType.Watch, string.Empty),
        _ => (TaskTriggerType.Manual, string.Empty),
    };
}
