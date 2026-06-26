using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Models;
using SyncMaid.Views;

namespace SyncMaid.ViewModels;

public partial class TaskNodeViewModel : ViewModelBase
{
    private readonly Func<TaskNodeViewModel, Task> _onEdit;
    private readonly Action<TaskNodeViewModel> _onDelete;
    private Window? _window;

    public TaskNodeViewModel(
        TaskModel task,
        Func<TaskNodeViewModel, Task> onEdit,
        Action<TaskNodeViewModel> onDelete)
    {
        Task = task;
        _onEdit = onEdit;
        _onDelete = onDelete;

        Children = new ObservableCollection<DestinationNodeViewModel>();
        foreach (var destination in task.Destinations)
        {
            Children.Add(new DestinationNodeViewModel(destination, EditLeaf, DeleteLeaf));
        }

        // Execute is only enabled with at least one destination; re-evaluate whenever
        // the children change (this is the bug the old WhenAnyValue(Count) didn't catch).
        Children.CollectionChanged += (_, _) => ExecuteCommand.NotifyCanExecuteChanged();
    }

    internal TaskModel Task { get; }

    // Name/Path come from the immutable model; editing replaces the whole node, so no
    // change notification is needed here.
    public string Name => Task.Name;
    public string Path => Task.Path;

    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    public void SetHostWindow(Window window) => _window = window;

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private void Execute()
    {
        // Placeholder until the engine is wired in (Phase 5).
        foreach (var destination in Children)
        {
            Console.WriteLine($"Executing sync from {Path} to {destination.Name} ({destination.Path})");
        }
    }

    private bool CanExecute() => Children.Count > 0;

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);

    [RelayCommand]
    private async Task AddDestination()
    {
        if (_window == null) return;

        var destination = await DestinationEditorWindow.ShowDialog(_window);
        if (destination != null)
        {
            Task.Destinations.Add(destination);
            Children.Add(new DestinationNodeViewModel(destination, EditLeaf, DeleteLeaf));
        }
    }

    private async Task EditLeaf(DestinationNodeViewModel node)
    {
        if (_window == null) return;

        var edited = await DestinationEditorWindow.ShowDialog(_window, node.Model);
        if (edited != null)
        {
            var index = Children.IndexOf(node);
            Children[index] = new DestinationNodeViewModel(edited, EditLeaf, DeleteLeaf);

            var modelIndex = Task.Destinations.IndexOf(node.Model);
            Task.Destinations[modelIndex] = edited;
        }
    }

    private void DeleteLeaf(DestinationNodeViewModel node)
    {
        Task.Destinations.Remove(node.Model);
        Children.Remove(node);
    }
}
