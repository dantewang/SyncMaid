#region

using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using DynamicData;
using ReactiveUI;
using SyncMaid.Models;
using SyncMaid.Views;

#endregion

namespace SyncMaid.ViewModels;

public class TaskNodeViewModel : ViewModelBase
{
    private Window? _window;
    internal readonly TaskModel _task;

    public TaskNodeViewModel(TaskModel task,
        Func<TaskNodeViewModel, Task> onEdit,
        Action<TaskNodeViewModel> onDelete)
    {
        _task = task;
        Children = new ObservableCollection<DestinationNodeViewModel>();

        // Convert model's destinations to ViewModels
        foreach (var dest in task.Destinations)
            Children.Add(new DestinationNodeViewModel(dest,
                EditLeaf,
                DeleteLeaf));

        var canExecute = this.WhenAnyValue(x => x.Children.Count)
            .Select(count => count > 0);

        ExecuteCommand = ReactiveCommand.Create(Execute, canExecute);
        EditCommand = ReactiveCommand.CreateFromTask(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
        AddDestinationCommand = ReactiveCommand.CreateFromTask(AddDestination);

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

    public void SetHostWindow(Window window)
    {
        _window = window;
    }

    private void Execute()
    {
        // Execute sync for all destinations
        foreach (var destination in Children)
        {
            Console.WriteLine($"Executing sync from {Path} to destination: {destination.Name}, Path: {destination.Path}");
        }
    }

    private async Task AddDestination()
    {
        if (_window == null) return;

        var destination = await DestinationEditorWindow.ShowDialog(_window);
        if (destination != null)
        {
            _task.Destinations.Add(destination);
            Children.Add(new DestinationNodeViewModel(destination,
                EditLeaf,
                DeleteLeaf));
        }
    }

    private async Task EditLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        if (_window == null) return;

        var destination = await DestinationEditorWindow.ShowDialog(_window,
            new DestinationModel(
                destinationNodeViewModel.Name,
                destinationNodeViewModel.Path,
                destinationNodeViewModel.Model.SyncAll,
                destinationNodeViewModel.Model.Strategy,
                destinationNodeViewModel.Model.Filters));

        if (destination != null)
        {
            var index = Children.IndexOf(destinationNodeViewModel);
            var newDestNode = new DestinationNodeViewModel(destination,
                EditLeaf,
                DeleteLeaf);
            Children[index] = newDestNode;

            var modelIndex = _task.Destinations.IndexOf(destinationNodeViewModel.Model);
            _task.Destinations[modelIndex] = destination;
        }
    }

    private void DeleteLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        _task.Destinations.Remove(destinationNodeViewModel.Model);
        Children.Remove(destinationNodeViewModel);
    }

    public void SetWindow(Window window)
    {
        _window = window;
    }
}