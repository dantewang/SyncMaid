using System;

namespace SyncMaid.ViewModels;

/// <summary>
/// Non-generic view of a modal dialog so the window can drive it from the keyboard without
/// knowing its result type: Esc cancels, Enter performs the default action.
/// </summary>
public interface IDialogViewModel
{
    /// <summary>Closes the dialog as cancelled (Esc / clicking away).</summary>
    void RequestCancel();

    /// <summary>Performs the default/confirm action if one applies and is valid (Enter).
    /// Returns true if it was handled, so the key isn't passed on.</summary>
    bool RequestAccept();
}

/// <summary>
/// Base for editor view models shown as in-window modal dialogs. Raises
/// <see cref="CloseRequested"/> with the result (or null on cancel); the dialog host
/// listens, hides the overlay, and completes the awaiting caller.
/// </summary>
public abstract class DialogViewModel<TResult> : ViewModelBase, IDialogViewModel
{
    /// <summary>Raised when the dialog should close: the result, or null if cancelled.</summary>
    public event Action<TResult?>? CloseRequested;

    protected void Close(TResult? result) => CloseRequested?.Invoke(result);

    /// <summary>Esc: close as cancelled (the default result). Dialogs whose cancel does more
    /// can override.</summary>
    public virtual void RequestCancel() => Close(default);

    /// <summary>Enter: no default action unless a dialog overrides (e.g. an editor runs OK).</summary>
    public virtual bool RequestAccept() => false;
}
