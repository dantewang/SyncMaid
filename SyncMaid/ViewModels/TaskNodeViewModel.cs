using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.Logging;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

public partial class TaskNodeViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly ISyncEngine _engine;
    private readonly ITriggerSourceFactory _triggerFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<TaskNodeViewModel, Task> _onEdit;
    private readonly Action<TaskNodeViewModel> _onDelete;
    private readonly Action _onChanged;
    private readonly Action<IReadOnlyList<DestinationSyncStatus>> _onStatusesUpdated;
    private readonly ILogger _logger;

    private ITriggerSource? _triggerSource;
    private int _running;   // 0 = idle, 1 = a sync is in progress (manual or triggered)

    [ObservableProperty]
    private bool _isExpanded = true;

    public TaskNodeViewModel(
        SyncTask task,
        IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses,
        IDialogService dialogs,
        ISyncEngine engine,
        ITriggerSourceFactory triggerFactory,
        IUiDispatcher dispatcher,
        Func<TaskNodeViewModel, Task> onEdit,
        Action<TaskNodeViewModel> onDelete,
        Action onChanged,
        Action<IReadOnlyList<DestinationSyncStatus>> onStatusesUpdated,
        ILogger logger)
    {
        Task = task;
        _dialogs = dialogs;
        _engine = engine;
        _triggerFactory = triggerFactory;
        _dispatcher = dispatcher;
        _onEdit = onEdit;
        _onDelete = onDelete;
        _onChanged = onChanged;
        _onStatusesUpdated = onStatusesUpdated;
        _logger = logger;

        Children = new ObservableCollection<DestinationNodeViewModel>();
        foreach (var destination in task.Destinations)
        {
            var status = statuses.TryGetValue(destination.Id, out var saved)
                ? saved
                : DestinationSyncStatus.Never(destination.Id);
            Children.Add(NewChild(destination, status));
        }

        Children.CollectionChanged += (_, _) =>
        {
            ExecuteCommand.NotifyCanExecuteChanged();
            RefreshHealth();
        };

        StartTrigger();
    }

    /// <summary>The current task. Replaced (immutably) whenever its destinations change.</summary>
    public SyncTask Task { get; private set; }

    // From the immutable task; the node is replaced on edit, so no notification needed.
    public string Name => Task.Name;
    public string Path => Task.SourcePath;

    /// <summary>Full name + path, shown as the sidebar tooltip since the row truncates both.</summary>
    public string SidebarTooltip => $"{Name}\n{Path}";

    public string TriggerText => Task.Trigger switch
    {
        ManualTrigger => "Manual",
        WatchTrigger => "Watching",
        ScheduledTrigger scheduled => $"Scheduled · {scheduled.CronExpression}",
        _ => "Manual",
    };

    public MaterialIconKind TriggerIconKind => Task.Trigger switch
    {
        WatchTrigger => MaterialIconKind.Eye,
        ScheduledTrigger => MaterialIconKind.ClockOutline,
        _ => MaterialIconKind.CursorDefaultClickOutline,
    };

    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    /// <summary>At-a-glance health shown on the (possibly collapsed) card header.</summary>
    public SyncOutcome HealthOutcome
    {
        get
        {
            if (Children.Count == 0) return SyncOutcome.Never;
            if (Children.Any(c => c.Outcome == SyncOutcome.Running)) return SyncOutcome.Running;
            if (Children.Any(c => c.Outcome == SyncOutcome.Failed)) return SyncOutcome.Failed;
            if (Children.Any(c => c.Outcome == SyncOutcome.Success)) return SyncOutcome.Success;
            return SyncOutcome.Never;
        }
    }

    public string HealthText
    {
        get
        {
            if (Children.Count == 0) return "No destinations";
            if (Children.Any(c => c.Outcome == SyncOutcome.Running)) return "Syncing…";
            var failed = Children.Count(c => c.Outcome == SyncOutcome.Failed);
            if (failed > 0) return $"{failed} of {Children.Count} failed";
            if (Children.All(c => c.Outcome == SyncOutcome.Success)) return "All synced";
            if (Children.Any(c => c.Outcome == SyncOutcome.Success)) return "Partly synced";
            return "Never run";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private Task Execute() => RunAsync();

    private bool CanExecute() => Children.Count > 0;

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);

    [RelayCommand]
    private async Task AddDestination()
    {
        var destination = await _dialogs.EditDestinationAsync(null);
        if (destination != null)
        {
            Children.Add(NewChild(destination, DestinationSyncStatus.Never(destination.Id)));
            RebuildAndPersist();
        }
    }

    private async Task EditLeaf(DestinationNodeViewModel node)
    {
        var edited = await _dialogs.EditDestinationAsync(node.Destination);
        if (edited != null)
        {
            // Id is preserved by the editor, so the existing status still applies.
            Children[Children.IndexOf(node)] = NewChild(edited, node.Status);
            RebuildAndPersist();
        }
    }

    private void DeleteLeaf(DestinationNodeViewModel node)
    {
        Children.Remove(node);
        RebuildAndPersist();
    }

    private DestinationNodeViewModel NewChild(Destination destination, DestinationSyncStatus status) =>
        new(destination, status, EditLeaf, DeleteLeaf);

    // Destinations are immutable on the task, so rebuild it from the child nodes.
    private void RebuildAndPersist()
    {
        Task = Task with { Destinations = Children.Select(child => child.Destination).ToList() };
        _onChanged();
    }

    private void RefreshHealth()
    {
        OnPropertyChanged(nameof(HealthOutcome));
        OnPropertyChanged(nameof(HealthText));
    }

    // Wires the task's trigger (scheduled/watch) to run the sync automatically. Manual
    // tasks get an inert source that never fires. A failure to start (e.g. a missing
    // watch directory) degrades to manual-only rather than crashing the app.
    private void StartTrigger()
    {
        try
        {
            _triggerSource = _triggerFactory.Create(Task.Trigger, Task.SourcePath);
            _triggerSource.Fired += OnTriggerFired;
            _triggerSource.Start();
        }
        catch (Exception exception)
        {
            // Degrade to manual-only rather than crashing, but no longer silently — a bad
            // watch path or cron would otherwise never run with no explanation.
            _logger.LogError(exception, "Failed to start the trigger for task '{Task}'.", Task.Name);
        }
    }

    private async void OnTriggerFired(object? sender, EventArgs e) => await RunAsync();

    // Single entry point for both manual and triggered runs; skips if one is already in
    // flight so overlapping triggers don't run concurrent syncs of the same task. VM
    // mutations are marshaled to the UI thread since triggers fire on background threads.
    private async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            _dispatcher.Post(() =>
            {
                foreach (var child in Children)
                {
                    child.MarkRunning();
                }

                RefreshHealth();
            });

            var statuses = await _engine.ExecuteAsync(Task);

            _dispatcher.Post(() =>
            {
                foreach (var status in statuses)
                {
                    ChildById(status.DestinationId)?.SetStatus(status);
                }

                RefreshHealth();
            });

            _onStatusesUpdated(statuses);
        }
        catch (Exception exception)
        {
            // Per-destination failures are already captured as statuses by the engine; this
            // catches an unexpected engine/dispatch failure so it is recorded, not swallowed.
            _logger.LogError(exception, "Sync run failed for task '{Task}'.", Task.Name);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private DestinationNodeViewModel? ChildById(Guid id) => Children.FirstOrDefault(c => c.Id == id);

    public void Dispose()
    {
        if (_triggerSource != null)
        {
            _triggerSource.Fired -= OnTriggerFired;
            _triggerSource.Dispose();
            _triggerSource = null;
        }
    }
}
