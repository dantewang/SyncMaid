using CommunityToolkit.Mvvm.Input;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// App-level view model backing the declarative system-tray icon in <c>App.axaml</c>: its
/// left-click and menu commands delegate to the <see cref="TrayController"/>. Set as
/// <c>Application.DataContext</c> at startup, since the controller needs the runtime window and
/// desktop lifetime that only exist once the framework has initialized.
/// </summary>
public partial class AppTrayViewModel : ViewModelBase
{
    private readonly TrayController _controller;

    public AppTrayViewModel(TrayController controller) => _controller = controller;

    /// <summary>Tray icon click and "Show main window" menu item.</summary>
    [RelayCommand]
    private void ShowMainWindow() => _controller.ShowMainWindow();

    /// <summary>"Exit" menu item.</summary>
    [RelayCommand]
    private void Exit() => _controller.Exit();
}
