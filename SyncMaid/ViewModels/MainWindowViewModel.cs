using System.Collections.ObjectModel;
using System.Diagnostics;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        Nodes = [];

        var task1 = new TaskModel("Task 1", @"D:\My Files\");

        task1.Destinations.Add(new DestinationModel("Destination 1", @"E:\Backups\My Phone Photos"));
        task1.Destinations.Add(new DestinationModel("Destination 2", @"\\nas-1\backups\photos"));

        var root1 = new TaskNodeViewModel(
            task1,
            EditRoot,
            DeleteRoot);

        Nodes.Add(root1);
    }

    public ObservableCollection<TaskNodeViewModel> Nodes { get; }

    private void EditRoot(TaskNodeViewModel taskNodeViewModel)
    {
        // Implement root edit logic
        Debug.WriteLine($"Editing root: {taskNodeViewModel.Name}");
    }

    private void DeleteRoot(TaskNodeViewModel taskNodeViewModel)
    {
        Nodes.Remove(taskNodeViewModel);
    }

    private void ExecuteLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        // Implement leaf execution logic
        Debug.WriteLine($"Executing leaf: {destinationNodeViewModel.Name}, Path: {destinationNodeViewModel.Path}");
    }

    private void EditLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        // Implement leaf edit logic
        Debug.WriteLine($"Editing leaf: {destinationNodeViewModel.Name}");
    }

    private void DeleteLeaf(DestinationNodeViewModel destinationNodeViewModel)
    {
        // Find the parent and remove the leaf node
        foreach (var root in Nodes)
            if (root.Children.Remove(destinationNodeViewModel))
                break;
    }
#pragma warning disable CA1822 // Mark members as static

#pragma warning restore CA1822 // Mark members as static
}