using System.Threading.Tasks;
using Avalonia.Controls;
using SyncMaid.Core.Model;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid.Services;

/// <summary>
/// Creates each editor window, hands it a view model, and bridges the view model's
/// <c>CloseRequested</c> signal to the window's modal result. This is the only place
/// that knows both the view models and the windows.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IFolderPickerService _folderPicker;

    public DialogService(IFolderPickerService folderPicker) => _folderPicker = folderPicker;

    /// <inheritdoc />
    public Task<SyncTask?> EditTaskAsync(SyncTask? existing)
    {
        var viewModel = new TaskEditorViewModel(_folderPicker, existing);
        var window = new TaskEditorWindow { DataContext = viewModel };
        viewModel.CloseRequested += result => window.Close(result);
        return ShowDialog<SyncTask>(window);
    }

    /// <inheritdoc />
    public Task<Destination?> EditDestinationAsync(Destination? existing)
    {
        var viewModel = new DestinationEditorViewModel(_folderPicker, existing);
        var window = new DestinationEditorWindow { DataContext = viewModel };
        viewModel.CloseRequested += result => window.Close(result);
        return ShowDialog<Destination>(window);
    }

    private static Task<T?> ShowDialog<T>(Window dialog) where T : class
    {
        var owner = WindowLocator.Active();
        return owner is not null
            ? dialog.ShowDialog<T?>(owner)
            : Task.FromResult<T?>(null);
    }
}
