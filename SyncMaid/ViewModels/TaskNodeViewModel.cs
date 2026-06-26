using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

public partial class TaskNodeViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly ISyncEngine _engine;
    private readonly Func<TaskNodeViewModel, Task> _onEdit;
    private readonly Action<TaskNodeViewModel> _onDelete;
    private readonly Action _onChanged;

    public TaskNodeViewModel(
        SyncTask task,
        IDialogService dialogs,
        ISyncEngine engine,
        Func<TaskNodeViewModel, Task> onEdit,
        Action<TaskNodeViewModel> onDelete,
        Action onChanged)
    {
        Task = task;
        _dialogs = dialogs;
        _engine = engine;
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
    }

    /// <summary>The current task. Replaced (immutably) whenever its destinations change.</summary>
    public SyncTask Task { get; private set; }

    // From the immutable task; the node is replaced on edit, so no notification needed.
    public string Name => Task.Name;
    public string Path => Task.SourcePath;

    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private Task Execute() => _engine.ExecuteAsync(Task);

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
}
