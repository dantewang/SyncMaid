using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Persistence;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly ITaskStore _store;
    private readonly ISyncEngine _engine;

    public MainWindowViewModel(IDialogService dialogs, ITaskStore store, ISyncEngine engine)
    {
        _dialogs = dialogs;
        _store = store;
        _engine = engine;

        Nodes = new ObservableCollection<TaskNodeViewModel>();
        foreach (var task in _store.Load())
        {
            Nodes.Add(CreateNode(task));
        }
    }

    public ObservableCollection<TaskNodeViewModel> Nodes { get; }

    [RelayCommand]
    private async Task AddTask()
    {
        var task = await _dialogs.EditTaskAsync(null);
        if (task != null)
        {
            Nodes.Add(CreateNode(task));
            Persist();
        }
    }

    private async Task EditTask(TaskNodeViewModel node)
    {
        var edited = await _dialogs.EditTaskAsync(node.Task);
        if (edited != null)
        {
            // The editor only edits the task's own fields; carry over its destinations.
            var merged = edited with { Destinations = node.Task.Destinations };
            Nodes[Nodes.IndexOf(node)] = CreateNode(merged);
            Persist();
        }
    }

    private void DeleteTask(TaskNodeViewModel node)
    {
        Nodes.Remove(node);
        Persist();
    }

    private TaskNodeViewModel CreateNode(SyncTask task) =>
        new(task, _dialogs, _engine, EditTask, DeleteTask, Persist);

    private void Persist() => _store.Save(Nodes.Select(node => node.Task).ToList());
}
