using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncMaid.Core.IO;
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
    private readonly IFileSystem? _fileSystem;

    /// <param name="fileSystem">Used by the destination workspace to preview where the
    /// source's files would go. Optional: without it the workspace simply offers no preview.</param>
    public DialogService(IFolderPickerService folderPicker, IDialogHost host, IFileSystem? fileSystem = null)
    {
        _folderPicker = folderPicker;
        _host = host;
        _fileSystem = fileSystem;
    }

    public Task<SyncTask?> EditTaskAsync(SyncTask? existing, Func<string, string?> sourceConflicts) =>
        _host.ShowAsync(new TaskEditorViewModel(_folderPicker, existing, sourceConflicts: sourceConflicts));

    public Task<IReadOnlyList<Destination>?> EditDestinationsAsync(
        SyncTask task,
        Guid? expand,
        bool startWithNewRule,
        Func<string, string?> crossTaskConflicts) =>
        _host.ShowAsync(new TaskWorkspaceViewModel(
            task, _folderPicker, crossTaskConflicts, expand, startWithNewRule, _fileSystem));

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = true) =>
        await _host.ShowAsync(new ConfirmViewModel(title, message, confirmLabel, isDestructive));
}
