using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SyncMaid.ViewModels;

namespace SyncMaid.Services;

/// <summary>
/// <see cref="IDialogHost"/> backed by an observable <see cref="CurrentDialog"/> that the
/// main window's overlay binds to. Only one dialog is shown at a time.
/// </summary>
public sealed partial class DialogHost : ObservableObject, IDialogHost
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private object? _currentDialog;

    public bool IsOpen => CurrentDialog is not null;

    public Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel)
    {
        var completion = new TaskCompletionSource<TResult?>();

        void OnClose(TResult? result)
        {
            viewModel.CloseRequested -= OnClose;
            CurrentDialog = null;
            completion.TrySetResult(result);
        }

        viewModel.CloseRequested += OnClose;
        CurrentDialog = viewModel;
        return completion.Task;
    }
}
