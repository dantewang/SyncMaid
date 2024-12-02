#region

using System;
using System.Reactive;
using System.Windows.Input;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class DestinationNodeViewModel : ViewModelBase
{
    private DestinationModel _model;

    public DestinationNodeViewModel(DestinationModel destination,
        Action<DestinationNodeViewModel> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        _model = destination;

        EditCommand = ReactiveCommand.Create(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));

        // Set up property changed notifications
        this.WhenAnyValue(x => x._model.Name)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Name)));
        this.WhenAnyValue(x => x._model.Path)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Path)));
    }

    public string Name => _model.Name;
    public string Path => _model.Path;

    public ReactiveCommand<Unit, Unit> EditCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public DestinationModel Model => _model;

    public void UpdateNameAndPath(string newName, string newPath)
    {
        _model = _model.WithUpdatedProperties(newName, newPath);
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(Path));
    }
}