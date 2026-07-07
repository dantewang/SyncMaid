using SyncMaid.Services;
using SyncMaid.UiTests.Fakes;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class AppTrayViewModelTests
{
    private static (AppTrayViewModel vm, FakeShellController shell) New()
    {
        var shell = new FakeShellController();
        var controller = new TrayController(new FakeAppSettingsService(), shell);
        return (new AppTrayViewModel(controller), shell);
    }

    [Fact]
    public void Show_main_window_command_activates_the_window()
    {
        var (vm, shell) = New();

        vm.ShowMainWindowCommand.Execute(null);

        Assert.Equal(1, shell.ShowCount);
    }

    [Fact]
    public void Exit_command_shuts_the_app_down()
    {
        var (vm, shell) = New();

        vm.ExitCommand.Execute(null);

        Assert.Equal(1, shell.ShutdownCount);
    }
}
