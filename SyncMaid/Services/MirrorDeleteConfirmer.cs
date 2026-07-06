using System.Threading.Tasks;
using Avalonia.Threading;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid.Services;

/// <summary>
/// Shows the mirror-delete confirmation as an independent top-level window (no owner), so it
/// appears even when the main window is hidden and never forces it open.
/// </summary>
public sealed class MirrorDeleteConfirmer : IMirrorDeleteConfirmer
{
    public Task<bool> ConfirmAsync(MirrorDeleteRequest request)
    {
        var completion = new TaskCompletionSource<bool>();

        // Marshal window creation to the UI thread (a background sync may trip the guard).
        Dispatcher.UIThread.Post(() =>
        {
            var viewModel = new ConfirmMirrorDeleteViewModel(request);
            var window = new ConfirmMirrorDeleteWindow { DataContext = viewModel };

            viewModel.Decided += result =>
            {
                completion.TrySetResult(result);
                window.Close();
            };
            window.Closed += (_, _) => completion.TrySetResult(false); // dismissed via the title bar = keep

            window.Show();
        });

        return completion.Task;
    }
}
