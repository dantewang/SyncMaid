using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.ViewModels;

namespace SyncMaid.Services;

/// <summary>
/// Opens the editor dialogs as in-window modals via <see cref="IDialogHost"/> and returns
/// the edited domain object, or null if cancelled. View models depend on this and stay
/// free of any view type.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IFolderPickerService _folderPicker;
    private readonly IDialogHost _host;

    public DialogService(IFolderPickerService folderPicker, IDialogHost host)
    {
        _folderPicker = folderPicker;
        _host = host;
    }

    public Task<SyncTask?> EditTaskAsync(SyncTask? existing) =>
        _host.ShowAsync(new TaskEditorViewModel(_folderPicker, existing));

    public Task<Destination?> EditDestinationAsync(Destination? existing, string sourcePath, bool hasSiblings) =>
        _host.ShowAsync(new DestinationEditorViewModel(_folderPicker, existing, sourcePath, hasSiblings: hasSiblings));

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", bool isDestructive = true) =>
        await _host.ShowAsync(new ConfirmViewModel(title, message, confirmLabel, isDestructive));
}
