namespace SyncMaid.Services;

/// <summary>
/// Coordinates the tray-icon behaviour: showing/hiding the main window, exiting, and the
/// close-to-tray decision. Deliberately free of Avalonia's <c>TrayIcon</c> so the hide-vs-exit
/// logic is unit-testable — <c>App</c> creates the actual tray icon and forwards its
/// menu/click events here.
/// </summary>
public sealed class TrayController
{
    private readonly IAppSettingsService _settings;
    private readonly IShellController _shell;

    public TrayController(IAppSettingsService settings, IShellController shell)
    {
        _settings = settings;
        _shell = shell;
    }

    /// <summary>Show/activate the main window — the tray icon click and "Show main window" menu item.</summary>
    public void ShowMainWindow() => _shell.ShowMainWindow();

    /// <summary>Exit the app — the tray "Exit" menu item.</summary>
    public void Exit() => _shell.Shutdown();

    /// <summary>
    /// Decides what happens when the user closes the main window. With close-to-tray enabled
    /// the window is hidden and the app keeps running; otherwise the app exits. Returns
    /// <c>true</c> when the close should be cancelled (i.e. it was hidden to the tray).
    /// </summary>
    public bool HandleMainWindowClosing()
    {
        if (_settings.CloseToTray)
        {
            _shell.HideMainWindow();
            return true;
        }

        _shell.Shutdown();
        return false;
    }
}
