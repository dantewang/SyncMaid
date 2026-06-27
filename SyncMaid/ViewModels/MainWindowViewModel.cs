using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        IDialogHost dialogHost)
    {
        _dialogs = dialogs;
        _store = store;
        _statusStore = statusStore;
        _engine = engine;
        _triggerFactory = triggerFactory;
        _dispatcher = dispatcher;
        _dialogHost = dialogHost;
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

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

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

    private void DeleteTask(TaskNodeViewModel node)
    {
        Nodes.Remove(node);
        node.Dispose();
        Persist();
    }

    private TaskNodeViewModel CreateNode(SyncTask task) =>
        new(task, _statuses, _dialogs, _engine, _triggerFactory, _dispatcher,
            EditTask, DeleteTask, Persist, OnStatusesUpdated)
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

            _statusStore.Save(_statuses);
        }
    }

    private void Persist() => _store.Save(Nodes.Select(node => node.Task).ToList());

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
