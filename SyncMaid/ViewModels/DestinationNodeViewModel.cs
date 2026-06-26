using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Models;

namespace SyncMaid.ViewModels;

public partial class DestinationNodeViewModel : ViewModelBase
{
    private readonly Func<DestinationNodeViewModel, Task> _onEdit;
    private readonly Action<DestinationNodeViewModel> _onDelete;

    public DestinationNodeViewModel(
        DestinationModel model,
        Func<DestinationNodeViewModel, Task> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        Model = model;
        _onEdit = onEdit;
        _onDelete = onDelete;
    }

    internal DestinationModel Model { get; }

    // From the immutable model; editing replaces the node, so no notification needed.
    public string Name => Model.Name;
    public string Path => Model.Path;

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);
}
