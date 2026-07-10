using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// Shared mechanics for folder-backed editors: identity preservation, common name/path
/// fields, folder browsing, cancellation, path-existence feedback, and Enter-to-accept.
/// </summary>
public abstract partial class EditorDialogViewModel<TResult> : DialogViewModel<TResult>
{
    private readonly IFolderPickerService _folderPicker;
    private readonly string _folderPickerTitle;
    private readonly Func<string, bool> _directoryExists;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPathHint))]
    private string _path = string.Empty;

    protected EditorDialogViewModel(
        IFolderPickerService folderPicker,
        string folderPickerTitle,
        Guid? existingId,
        string? initialName,
        string? initialPath,
        Func<string, bool>? directoryExists)
    {
        _folderPicker = folderPicker;
        _folderPickerTitle = folderPickerTitle;
        _directoryExists = directoryExists ?? System.IO.Directory.Exists;
        EditorId = existingId ?? Guid.NewGuid();
        _name = initialName ?? string.Empty;
        _path = initialPath ?? string.Empty;
    }

    /// <summary>The stable model identity, preserved for edits and generated for new items.</summary>
    protected Guid EditorId { get; }

    /// <summary>The derived editor's generated accept command.</summary>
    protected abstract IRelayCommand AcceptCommand { get; }

    /// <summary>Non-blocking warning for a missing directory or an editor-specific path risk.</summary>
    public bool ShowPathHint => HasAdditionalPathWarning
        || (!string.IsNullOrWhiteSpace(Path) && !_directoryExists(Path));

    /// <summary>Allows a derived editor to contribute a stronger path warning.</summary>
    protected virtual bool HasAdditionalPathWarning => false;

    partial void OnNameChanged(string value) => AcceptCommand.NotifyCanExecuteChanged();

    partial void OnPathChanged(string value)
    {
        AcceptCommand.NotifyCanExecuteChanged();
        OnEditorPathChanged();
    }

    /// <summary>Lets an editor invalidate additional path-dependent display properties.</summary>
    protected virtual void OnEditorPathChanged()
    {
    }

    /// <summary>Enter saves when the derived editor's accept command is valid.</summary>
    public override bool RequestAccept()
    {
        if (!AcceptCommand.CanExecute(null))
        {
            return false;
        }

        AcceptCommand.Execute(null);
        return true;
    }

    [RelayCommand]
    private void Cancel() => RequestCancel();

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await _folderPicker.PickFolderAsync(_folderPickerTitle);
        if (folder is not null)
        {
            Path = folder;
        }
    }
}
