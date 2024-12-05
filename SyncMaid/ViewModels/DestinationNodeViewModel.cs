#region

using System;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using SyncMaid.Models;

#endregion

namespace SyncMaid.ViewModels;

public class DestinationNodeViewModel : ViewModelBase
{
    internal readonly DestinationModel Model;

    public DestinationNodeViewModel(DestinationModel model,
        Func<DestinationNodeViewModel, Task> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        Model = model;

        ExecuteCommand = ReactiveCommand.Create(Execute);
        EditCommand = ReactiveCommand.CreateFromTask(() => onEdit(this));
        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));

        // Set up property changed notifications for Name and Path
        this.WhenAnyValue(x => x.Model.Name)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Name)));
        this.WhenAnyValue(x => x.Model.Path)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Path)));
    }

    public string Name => Model.Name;
    public string Path => Model.Path;

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> EditCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    private void Execute()
    {
        Console.WriteLine($"Executing sync to destination: {Name}, Path: {Path}");
    }
}