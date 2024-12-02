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

        ExecuteCommand = ReactiveCommand.Create(Execute);
        EditCommand = ReactiveCommand.Create(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }

    public string Name => _task.Name;
    public string Path => _task.Path;
    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    public ICommand ExecuteCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    private void Execute()
    {
        // Execute sync for all destinations
        foreach (var destination in Children)
        {
            Debug.WriteLine($"Executing sync from {Path} to destination: {destination.Name}, Path: {destination.Path}");
        }
    }

    private void EditLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        // Implement leaf edit logic
        Debug.WriteLine($"Editing leaf: {destinationNodeViewModel.Name}");
    }

    private void DeleteLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        // Find the parent and remove the leaf node
        Debug.WriteLine($"Deleting leaf: {destinationNodeViewModel.Name}");
    }
}