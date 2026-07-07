using SyncMaid.Services;
using SyncMaid.UiTests.Fakes;

namespace SyncMaid.UiTests.ViewModels;

public class TrayControllerTests
{
    private static (TrayController tray, FakeShellController shell) New(bool closeToTray)
    {
        var shell = new FakeShellController();
        var tray = new TrayController(new FakeAppSettingsService { CloseToTray = closeToTray }, shell);
        return (tray, shell);
    }

    [Fact]
    public void Closing_with_close_to_tray_hides_and_cancels_the_close()
    {
        var (tray, shell) = New(closeToTray: true);

        var cancel = tray.HandleMainWindowClosing();

        Assert.True(cancel);                 // the window stays open (hidden)
        Assert.Equal(1, shell.HideCount);
        Assert.Equal(0, shell.ShutdownCount);
    }

    [Fact]
    public void Closing_without_close_to_tray_exits_the_app()
    {
        var (tray, shell) = New(closeToTray: false);

        var cancel = tray.HandleMainWindowClosing();

        Assert.False(cancel);                // the close proceeds
        Assert.Equal(1, shell.ShutdownCount);
        Assert.Equal(0, shell.HideCount);
    }

    [Fact]
    public void Show_main_window_activates_the_window()
    {
        var (tray, shell) = New(closeToTray: true);

        tray.ShowMainWindow();

        Assert.Equal(1, shell.ShowCount);
    }

    [Fact]
    public void Exit_shuts_down_regardless_of_close_to_tray()
    {
        var (tray, shell) = New(closeToTray: true);

        tray.Exit();

        Assert.Equal(1, shell.ShutdownCount);
        Assert.Equal(0, shell.HideCount);
    }
}
