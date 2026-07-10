using System.Threading.Tasks;
using SyncMaid.Core.Model;

namespace SyncMaid.Services;

/// <summary>
/// Opens the editor dialogs and returns the edited domain object, or null if cancelled.
/// View models depend on this instead of constructing windows, so they stay free of any
/// view type and remain unit-testable with a fake.
/// </summary>
public interface IDialogService
{
    /// <param name="existing">The task to edit, or null to create a new one.</param>
    Task<SyncTask?> EditTaskAsync(SyncTask? existing);

    /// <param name="existing">The destination to edit, or null to create a new one.</param>
    /// <param name="sourcePath">The owning task's source path, used to reject destructive Move targets.</param>
    Task<Destination?> EditDestinationAsync(Destination? existing, string sourcePath);

    /// <summary>Shows a modal yes/no confirmation. Returns true only if the user confirms.</summary>
    /// <param name="title">Dialog heading.</param>
    /// <param name="message">Explanatory body text.</param>
    /// <param name="confirmLabel">Label of the confirming button (e.g. "Delete").</param>
    /// <param name="isDestructive">When true, the confirm button is styled as destructive.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", bool isDestructive = true);
}
