#region

using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class TaskNodeViewModel : ViewModelBase
{
    private static readonly Random _random = new();
    private TaskModel _task;

    public TaskNodeViewModel(TaskModel task,
        Action<TaskNodeViewModel> onEdit,
        Action<TaskNodeViewModel> onDelete)
    {
        _task = task;
        Children = [];

        // Convert model's destinations to ViewModels
        foreach (var dest in task.Destinations)
            Children.Add(new DestinationNodeViewModel(dest,
                EditLeaf,
                DeleteLeaf));

        var canExecute = this.WhenAnyValue(x => x.Children.Count)
            .Select(count => count > 0);

        ExecuteCommand = ReactiveCommand.Create(Execute, canExecute);
        EditCommand = ReactiveCommand.Create(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
        AddDestinationCommand = ReactiveCommand.Create(AddDestination);

        // Set up property changed notifications for Name and Path
        this.WhenAnyValue(x => x._task.Name)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Name)));
        this.WhenAnyValue(x => x._task.Path)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Path)));
    }

    public string Name => _task.Name;
    public string Path => _task.Path;
    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> EditCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> AddDestinationCommand { get; }

    private void Execute()
    {
        // Execute sync for all destinations
        foreach (var destination in Children)
        {
            Console.WriteLine($"Executing sync from {Path} to destination: {destination.Name}, Path: {destination.Path}");
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
        _task = _task.WithUpdatedProperties(newName, newPath);
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