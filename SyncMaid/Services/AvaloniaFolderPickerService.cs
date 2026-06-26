using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace SyncMaid.Services;

/// <summary>
/// <see cref="IFolderPickerService"/> backed by Avalonia's storage provider, attached to
/// whichever window is currently active.
/// </summary>
public sealed class AvaloniaFolderPickerService : IFolderPickerService
{
    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title)
    {
        var window = WindowLocator.Active();
        if (window is null)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
