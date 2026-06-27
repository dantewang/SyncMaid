using System;

namespace SyncMaid.ViewModels;

/// <summary>
/// Base for editor view models shown as in-window modal dialogs. Raises
/// <see cref="CloseRequested"/> with the result (or null on cancel); the dialog host
/// listens, hides the overlay, and completes the awaiting caller.
/// </summary>
public abstract class DialogViewModel<TResult> : ViewModelBase
{
    /// <summary>Raised when the dialog should close: the result, or null if cancelled.</summary>
    public event Action<TResult?>? CloseRequested;

    protected void Close(TResult? result) => CloseRequested?.Invoke(result);
}
