#region

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class TaskNodeViewModel : ViewModelBase
{
    private readonly TaskModel _task;
    private static readonly Random _random = new();

    public TaskNodeViewModel(TaskModel task,
        Action<TaskNodeViewModel> onEdit,
        Action<TaskNodeViewModel> onDelete)
    {
        _task = task;
        Children = new ObservableCollection<DestinationNodeViewModel>();

        // Convert model's destinations to ViewModels
        foreach (var dest in task.Destinations)
            Children.Add(new DestinationNodeViewModel(dest,
                EditLeaf,
                DeleteLeaf));

        ExecuteCommand = ReactiveCommand.Create(Execute);
        EditCommand = ReactiveCommand.Create(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
        AddDestinationCommand = ReactiveCommand.Create(AddDestination);
    }

    public string Name => _task.Name;
    public string Path => _task.Path;
    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    public ICommand ExecuteCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AddDestinationCommand { get; }

    private void Execute()
    {
        // Execute sync for all destinations
        foreach (var destination in Children)
        {
            Debug.WriteLine($"Executing sync from {Path} to destination: {destination.Name}, Path: {destination.Path}");
        }
    }

    private void AddDestination()
    {
        var randomName = $"Destination {_random.Next(1, 1000)}";
        var randomPath = $@"D:\Random\Path\{_random.Next(1, 1000)}";
        
        var destModel = new DestinationModel(randomName, randomPath);
        _task.Destinations.Add(destModel);
        
        Children.Add(new DestinationNodeViewModel(destModel,
            EditLeaf,
            DeleteLeaf));
    }

    public void UpdateNameAndPath(string newName, string newPath)
    {
        _task.Name = newName;
        _task.Path = newPath;
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(Path));
    }

    private void EditLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        var randomName = $"Destination {_random.Next(1, 1000)}";
        var randomPath = $@"D:\Random\Path\{_random.Next(1, 1000)}";
        destinationNodeViewModel.UpdateNameAndPath(randomName, randomPath);
    }

    private void DeleteLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        _task.Destinations.Remove(destinationNodeViewModel.Model);
        Children.Remove(destinationNodeViewModel);
    }
}