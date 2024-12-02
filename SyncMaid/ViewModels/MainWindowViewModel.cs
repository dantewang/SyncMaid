#region

using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private static readonly Random _random = new();

    public MainWindowViewModel()
    {
        Nodes = [];
        AddTaskCommand = ReactiveCommand.Create(AddTask);
    }

    public ObservableCollection<TaskNodeViewModel> Nodes { get; }
    public ReactiveCommand<Unit, Unit> AddTaskCommand { get; }

    private void AddTask()
    {
        var randomName = $"Task {_random.Next(1, 1000)}";
        var randomPath = $@"C:\Random\Source\{_random.Next(1, 1000)}";
        
        var task = new TaskModel(randomName, randomPath);
        var taskNode = new TaskNodeViewModel(task, EditTask, DeleteTask);
        
        Nodes.Add(taskNode);
    }

    private void EditTask(TaskNodeViewModel taskNodeViewModel)
    {
        var randomName = $"Task {_random.Next(1, 1000)}";
        var randomPath = $@"C:\Random\Source\{_random.Next(1, 1000)}";
        taskNodeViewModel.UpdateNameAndPath(randomName, randomPath);
    }

    private void DeleteTask(TaskNodeViewModel taskNodeViewModel)
    {
        Nodes.Remove(taskNodeViewModel);
    }
}