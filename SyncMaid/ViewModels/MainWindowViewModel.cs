using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly ITaskStore _store;
    private readonly IStatusStore _statusStore;
    private readonly ISyncEngine _engine;
    private readonly ITriggerSourceFactory _triggerFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogHost _dialogHost;
    private readonly IAutoStartService _autoStart;
    private readonly IMirrorDeleteConfirmer _confirmer;
    private readonly IAppSettingsService _appSettings;
    private readonly IConfigLocationService _configLocation;
    private readonly IAppRestartService _restart;
    private readonly ILogger _logger;
    private readonly ILogger _nodeLogger;
    private readonly Dictionary<Guid, DestinationSyncStatus> _statuses;
    private readonly Lock _statusGate = new();

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private TaskNodeViewModel? _selectedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandCollapseLabel))]
    private bool _allExpanded = true;

    public MainWindowViewModel(
        IDialogService dialogs,
        ITaskStore store,
        IStatusStore statusStore,
        ISyncEngine engine,
        ITriggerSourceFactory triggerFactory,
        IUiDispatcher dispatcher,
        IDialogHost dialogHost,
        IAutoStartService autoStart,
        IMirrorDeleteConfirmer confirmer,
        IAppSettingsService appSettings,
        IConfigLocationService configLocation,
        IAppRestartService restart,
        ILoggerFactory loggerFactory)
    {
        _dialogs = dialogs;
        _store = store;
        _statusStore = statusStore;
        _engine = engine;
        _triggerFactory = triggerFactory;
        _dispatcher = dispatcher;
        _dialogHost = dialogHost;
        _autoStart = autoStart;
        _confirmer = confirmer;
        _appSettings = appSettings;
        _configLocation = configLocation;
        _restart = restart;
        _logger = loggerFactory.CreateLogger<MainWindowViewModel>();
        _nodeLogger = loggerFactory.CreateLogger<TaskNodeViewModel>();
        _statuses = new Dictionary<Guid, DestinationSyncStatus>(statusStore.Load());

        Nodes = new ObservableCollection<TaskNodeViewModel>();
        foreach (var task in _store.Load())
        {
            Nodes.Add(CreateNode(task));
        }

        SelectedTask = Nodes.FirstOrDefault();
    }

    public ObservableCollection<TaskNodeViewModel> Nodes { get; }

    /// <summary>The in-window modal host; the view binds an overlay to its CurrentDialog.</summary>
    public IDialogHost DialogHost => _dialogHost;

    public string ExpandCollapseLabel => AllExpanded ? "Collapse all" : "Expand all";

    /// <summary>Recomputes every task's next-run label. Driven by a one-minute UI timer in the
    /// view so the relative "next run in 2 h" badges stay current without per-node timers.</summary>
    public void RefreshSchedules()
    {
        foreach (var node in Nodes)
        {
            node.RefreshNextRun();
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    // Title-bar gear button → the in-window Settings modal (start with Windows, close to tray).
    [RelayCommand]
    private async Task OpenSettings() =>
        await _dialogHost.ShowAsync(new SettingsViewModel(_autoStart, _appSettings, _configLocation, _restart));

    [RelayCommand]
    private void ToggleExpandAll()
    {
        AllExpanded = !AllExpanded;
        foreach (var node in Nodes)
        {
            node.IsExpanded = AllExpanded;
        }
    }

    [RelayCommand]
    private void RunAll()
    {
        foreach (var node in Nodes)
        {
            if (node.ExecuteCommand.CanExecute(null))
            {
                node.ExecuteCommand.Execute(null);
            }
        }
    }

    [RelayCommand]
    private async Task AddTask()
    {
        var task = await _dialogs.EditTaskAsync(null);
        if (task != null)
        {
            var node = CreateNode(task);
            Nodes.Add(node);
            SelectedTask = node;
            Persist();
        }
    }

    private async Task EditTask(TaskNodeViewModel node)
    {
        var edited = await _dialogs.EditTaskAsync(node.Task);
        if (edited != null)
        {
            // The editor preserves the id and edits task fields only; carry destinations.
            var merged = edited with { Destinations = node.Task.Destinations };
            var index = Nodes.IndexOf(node);
            var replacement = CreateNode(merged);
            Nodes[index] = replacement;
            node.Dispose();
            if (ReferenceEquals(SelectedTask, node))
            {
                SelectedTask = replacement;
            }

            Persist();
        }
    }

    private async Task DeleteTask(TaskNodeViewModel node)
    {
        var count = node.Children.Count;
        var suffix = count == 1 ? "its 1 destination" : $"its {count} destinations";
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete task?",
            $"Delete the task \"{node.Name}\" and {suffix}? This can't be undone.",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        Nodes.Remove(node);
        node.Dispose();
        Persist();
    }

    private TaskNodeViewModel CreateNode(SyncTask task) =>
        new(task, _statuses, _dialogs, _engine, _triggerFactory, _dispatcher,
            EditTask, DeleteTask, Persist, OnStatusesUpdated, _nodeLogger, _confirmer)
        {
            IsExpanded = AllExpanded,
        };

    // Merges a completed run's statuses into the saved set. May be called off the UI thread.
    private void OnStatusesUpdated(IReadOnlyList<DestinationSyncStatus> statuses)
    {
        lock (_statusGate)
        {
            foreach (var status in statuses)
            {
                _statuses[status.DestinationId] = status;
            }

            try
            {
                _statusStore.Save(_statuses);
            }
            catch (Exception exception)
            {
                // A failed status save is not worth crashing over; the atomic write left the
                // previous file intact and the next run will retry.
                _logger.LogError(exception, "Failed to save sync statuses.");
            }
        }
    }

    private void Persist()
    {
        var tasks = Nodes.Select(node => node.Task).ToList();
        PruneOrphanedStatuses(tasks);

        try
        {
            _store.Save(tasks);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save tasks.");
        }
    }

    // Drops persisted statuses whose destination no longer exists (a deleted task/destination),
    // so status.json doesn't accumulate orphans forever. Only rewrites the file if something
    // was actually removed.
    private void PruneOrphanedStatuses(IReadOnlyList<SyncTask> tasks)
    {
        var knownIds = tasks.SelectMany(task => task.Destinations).Select(destination => destination.Id).ToHashSet();

        lock (_statusGate)
        {
            var orphans = _statuses.Keys.Where(id => !knownIds.Contains(id)).ToList();
            if (orphans.Count == 0)
            {
                return;
            }

            foreach (var id in orphans)
            {
                _statuses.Remove(id);
            }

            try
            {
                _statusStore.Save(_statuses);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to prune orphaned sync statuses.");
            }
        }
    }

    partial void OnSelectedTaskChanged(TaskNodeViewModel? value)
    {
        if (value != null)
        {
            value.IsExpanded = true;
        }
    }

    public void Dispose()
    {
        foreach (var node in Nodes)
        {
            node.Dispose();
        }
    }
}
