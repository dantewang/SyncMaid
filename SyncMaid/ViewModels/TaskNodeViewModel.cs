using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
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
    private readonly Func<TaskNodeViewModel, Task> _onEdit;
    private readonly Action<TaskNodeViewModel> _onDelete;
    private readonly Action _onChanged;

    private ITriggerSource? _triggerSource;
    private int _running;   // 0 = idle, 1 = a sync is in progress (manual or triggered)

    public TaskNodeViewModel(
        SyncTask task,
        IDialogService dialogs,
        ISyncEngine engine,
        ITriggerSourceFactory triggerFactory,
        Func<TaskNodeViewModel, Task> onEdit,
        Action<TaskNodeViewModel> onDelete,
        Action onChanged)
    {
        Task = task;
        _dialogs = dialogs;
        _engine = engine;
        _triggerFactory = triggerFactory;
        _onEdit = onEdit;
        _onDelete = onDelete;
        _onChanged = onChanged;

        Children = new ObservableCollection<DestinationNodeViewModel>();
        foreach (var destination in task.Destinations)
        {
            Children.Add(new DestinationNodeViewModel(destination, EditLeaf, DeleteLeaf));
        }

        // Execute is enabled only with at least one destination; re-evaluate on change.
        Children.CollectionChanged += (_, _) => ExecuteCommand.NotifyCanExecuteChanged();

        StartTrigger();
    }

    /// <summary>The current task. Replaced (immutably) whenever its destinations change.</summary>
    public SyncTask Task { get; private set; }

    // From the immutable task; the node is replaced on edit, so no notification needed.
    public string Name => Task.Name;
    public string Path => Task.SourcePath;

    public ObservableCollection<DestinationNodeViewModel> Children { get; }

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
            Children.Add(new DestinationNodeViewModel(destination, EditLeaf, DeleteLeaf));
            RebuildAndPersist();
        }
    }

    private async Task EditLeaf(DestinationNodeViewModel node)
    {
        var edited = await _dialogs.EditDestinationAsync(node.Destination);
        if (edited != null)
        {
            Children[Children.IndexOf(node)] = new DestinationNodeViewModel(edited, EditLeaf, DeleteLeaf);
            RebuildAndPersist();
        }
    }

    private void DeleteLeaf(DestinationNodeViewModel node)
    {
        Children.Remove(node);
        RebuildAndPersist();
    }

    // Destinations are immutable on the task, so rebuild it from the child nodes.
    private void RebuildAndPersist()
    {
        Task = Task with { Destinations = Children.Select(child => child.Destination).ToList() };
        _onChanged();
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
        catch
        {
            // TODO: surface trigger-start failures in the UI.
        }
    }

    private async void OnTriggerFired(object? sender, EventArgs e) => await RunAsync();

    // Single entry point for both manual and triggered runs; skips if one is already
    // in flight so overlapping triggers don't run concurrent syncs of the same task.
    private async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            await _engine.ExecuteAsync(Task);
        }
        catch
        {
            // TODO: surface sync errors in the UI.
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

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
