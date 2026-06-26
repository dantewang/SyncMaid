using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Model;

namespace SyncMaid.ViewModels;

public partial class DestinationNodeViewModel : ViewModelBase
{
    private readonly Func<DestinationNodeViewModel, Task> _onEdit;
    private readonly Action<DestinationNodeViewModel> _onDelete;

    public DestinationNodeViewModel(
        Destination destination,
        Func<DestinationNodeViewModel, Task> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        Destination = destination;
        _onEdit = onEdit;
        _onDelete = onDelete;
    }

    /// <summary>The wrapped immutable destination.</summary>
    public Destination Destination { get; }

    public string Name => Destination.Name;
    public string Path => Destination.Path;

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);
}
