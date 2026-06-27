using System.Threading.Tasks;
using SyncMaid.ViewModels;

namespace SyncMaid.Services;

/// <summary>
/// Hosts a single in-window modal dialog. The main window binds an overlay to
/// <see cref="CurrentDialog"/>; <see cref="ShowAsync{TResult}"/> displays a dialog view
/// model and completes when it closes.
/// </summary>
public interface IDialogHost
{
    /// <summary>The dialog view model currently shown, or null when none is open.</summary>
    object? CurrentDialog { get; }

    /// <summary>True while a dialog is shown — bound to the overlay's visibility.</summary>
    bool IsOpen { get; }

    /// <summary>Shows <paramref name="viewModel"/> as a modal and returns its result (null if cancelled).</summary>
    Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel);
}
