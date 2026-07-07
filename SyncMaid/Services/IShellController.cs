namespace SyncMaid.Services;

/// <summary>
/// The window/app-lifetime operations the tray needs, abstracted away from Avalonia so the
/// <see cref="TrayController"/> (and its hide-vs-exit decision) can be unit-tested without a
/// real <c>Window</c> or desktop lifetime. The concrete implementation lives in <c>App</c>.
/// </summary>
public interface IShellController
{
    /// <summary>Shows and activates the main window, restoring it if minimized.</summary>
    void ShowMainWindow();

    /// <summary>Hides the main window (the app keeps running in the tray).</summary>
    void HideMainWindow();

    /// <summary>Exits the application explicitly.</summary>
    void Shutdown();
}
