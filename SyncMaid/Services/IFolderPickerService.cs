using System.Threading.Tasks;

namespace SyncMaid.Services;

/// <summary>
/// Lets view models ask the user to pick a folder without touching any UI type. The
/// view model gets back a plain path string (or null if cancelled).
/// </summary>
public interface IFolderPickerService
{
    /// <param name="title">Dialog title shown to the user.</param>
    /// <returns>The chosen folder's local path, or null if the user cancelled.</returns>
    Task<string?> PickFolderAsync(string title);
}
