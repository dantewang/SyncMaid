using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Views;

namespace SyncMaid.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private Window? _window;

    public MainWindowViewModel()
    {
        Nodes = new ObservableCollection<TaskNodeViewModel>();
    }

    public ObservableCollection<TaskNodeViewModel> Nodes { get; }

    [RelayCommand]
    private async Task AddTask()
    {
        if (_window == null) return;

        var task = await TaskEditorWindow.ShowDialog(_window);
        if (task != null)
        {
            var taskNode = new TaskNodeViewModel(task, EditTask, DeleteTask);
            taskNode.SetHostWindow(_window);
            Nodes.Add(taskNode);
        }
    }

    private async Task EditTask(TaskNodeViewModel taskNodeViewModel)
    {
        if (_window == null) return;

        var task = await TaskEditorWindow.ShowDialog(_window, taskNodeViewModel.Task);
        if (task != null)
        {
            var index = Nodes.IndexOf(taskNodeViewModel);
            var newTaskNode = new TaskNodeViewModel(task, EditTask, DeleteTask);
            newTaskNode.SetHostWindow(_window);
            Nodes[index] = newTaskNode;
        }
    }

    private void DeleteTask(TaskNodeViewModel taskNodeViewModel)
    {
        Nodes.Remove(taskNodeViewModel);
    }

    public void SetHostWindow(Window window)
    {
        _window = window;
        foreach (var task in Nodes)
        {
            task.SetHostWindow(window);
        }
    }
}
