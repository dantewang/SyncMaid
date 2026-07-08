using CommunityToolkit.Mvvm.Input;

namespace SyncMaid.ViewModels;

/// <summary>
/// A small yes/no confirmation shown as an in-window modal (the <see cref="DialogViewModel{T}"/>
/// pattern). Used to guard destructive, single-click actions like deleting a task or
/// destination. Returns true if confirmed, false if cancelled. In-window (not an independent
/// window) is correct here: these are only ever triggered from a visible main window.
/// </summary>
public partial class ConfirmViewModel : DialogViewModel<bool>
{
    public ConfirmViewModel(string title, string message, string confirmLabel = "Delete", bool isDestructive = true)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
    }

    public string Title { get; }
    public string Message { get; }

    /// <summary>Label of the confirming button (e.g. "Delete").</summary>
    public string ConfirmLabel { get; }

    /// <summary>When true the confirm button is styled as destructive (red).</summary>
    public bool IsDestructive { get; }

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}
