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
    Task<Destination?> EditDestinationAsync(Destination? existing);
}
