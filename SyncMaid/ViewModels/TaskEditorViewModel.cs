using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Edits a task's own fields (name, source, trigger). Destinations are managed by the
/// task node, not here. Raises <see cref="CloseRequested"/> with the result (or null on
/// cancel) instead of touching the window, so it stays view-free and testable.
/// </summary>
public partial class TaskEditorViewModel : EditorDialogViewModel<SyncTask>
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    [NotifyPropertyChangedFor(nameof(IsScheduledTrigger))]
    private TaskTriggerType _selectedTriggerType = TaskTriggerType.Manual;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OKCommand))]
    [NotifyPropertyChangedFor(nameof(CronPreview))]
    private string _cronExpression = string.Empty;

    /// <param name="directoryExists">Directory probe, injectable for tests;
    /// defaults to <see cref="System.IO.Directory.Exists"/> (never throws — returns false
    /// for invalid/partial input, so it is safe to call while the user types).</param>
    private readonly Func<string, string?>? _sourceConflicts;

    public TaskEditorViewModel(
        IFolderPickerService folderPicker,
        SyncTask? existing = null,
        Func<string, bool>? directoryExists = null,
        Func<string, string?>? sourceConflicts = null)
        : base(
            folderPicker,
            Strings.Dialog_SelectSourceFolder,
            existing?.Id,
            existing?.Name,
            existing?.SourcePath,
            directoryExists)
    {
        _sourceConflicts = sourceConflicts;
        TriggerTypes = Enum.GetValues<TaskTriggerType>();

        if (existing != null)
        {
            (_selectedTriggerType, _cronExpression) = FromTrigger(existing.Trigger);
        }
    }

    // Task shape convention (AGENT.md): tasks never share same-kind paths. The probe is
    // supplied by the task-list owner and already excludes the task being edited.
    private string? SourceConflictName =>
        string.IsNullOrWhiteSpace(Path) ? null : _sourceConflicts?.Invoke(Path);

    /// <summary>Explains a blocked source overlap, or the non-blocking missing-folder hint.</summary>
    public string PathHintText => SourceConflictName is { } conflictingTask
        ? Localizer.Format(Strings.TaskEditor_SourceOverlapHintFormat, conflictingTask)
        : Strings.TaskEditor_MissingFolderHint;

    protected override bool HasAdditionalPathWarning => SourceConflictName is not null;

    protected override void OnEditorPathChanged() => OnPropertyChanged(nameof(PathHintText));

    public TaskTriggerType[] TriggerTypes { get; }

    public bool IsScheduledTrigger => SelectedTriggerType == TaskTriggerType.Scheduled;

    /// <summary>Plain-language feedback for the cron field: validity and the next run time.</summary>
    public string CronPreview
    {
        get
        {
            if (!CronSchedule.IsValid(CronExpression))
            {
                return Strings.TaskEditor_CronInvalid;
            }

            var next = CronSchedule.NextOccurrenceUtc(CronExpression, DateTime.UtcNow);
            return next is null
                ? Strings.TaskEditor_CronNoUpcoming
                : Localizer.Format(Strings.TaskEditor_CronNextRunFormat,
                    next.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void OK() =>
        // Destinations are merged in by the caller; a fresh task carries none.
        Close(new SyncTask(Name, Path, ToTrigger(), []) { Id = EditorId });

    protected override IRelayCommand AcceptCommand => OKCommand;

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Path)
        && SourceConflictName is null
        && (SelectedTriggerType != TaskTriggerType.Scheduled || CronSchedule.IsValid(CronExpression));

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
