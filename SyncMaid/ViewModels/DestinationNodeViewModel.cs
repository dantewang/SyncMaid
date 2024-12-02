#region

using System;
using System.Windows.Input;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class DestinationNodeViewModel : ViewModelBase
{
    public DestinationNodeViewModel(DestinationModel destination,
        Action<DestinationNodeViewModel> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        Model = destination;

        EditCommand = ReactiveCommand.Create(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }

    public string Name => Model.Name;
    public string Path => Model.Path;

    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    // Reference to the underlying model when needed
    public DestinationModel Model { get; }
}