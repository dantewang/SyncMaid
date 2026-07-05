using SyncMaid.Services;
using SyncMaid.UiTests.Fakes;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Reflects_enabled_state_without_writing_on_load()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Enabled };

        var vm = new SettingsViewModel(service);

        Assert.True(vm.StartWithWindows);
        Assert.False(vm.IsDisabledByWindows);
        Assert.Equal(0, service.EnableCount);   // seeding state must not write to the registry
        Assert.Equal(0, service.DisableCount);
    }

    [Fact]
    public void Reflects_disabled_state()
    {
        var vm = new SettingsViewModel(new FakeAutoStartService { State = AutoStartState.Disabled });

        Assert.False(vm.StartWithWindows);
        Assert.False(vm.IsDisabledByWindows);
    }

    [Fact]
    public void Toggling_on_enables_autostart_once()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Disabled };
        var vm = new SettingsViewModel(service);

        vm.StartWithWindows = true;

        Assert.Equal(1, service.EnableCount);
        Assert.Equal(0, service.DisableCount);
    }

    [Fact]
    public void Toggling_off_disables_autostart_once()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Enabled };
        var vm = new SettingsViewModel(service);

        vm.StartWithWindows = false;

        Assert.Equal(1, service.DisableCount);
        Assert.Equal(0, service.EnableCount);
    }

    [Fact]
    public void Disabled_by_windows_shows_the_notice_and_reads_as_off()
    {
        var vm = new SettingsViewModel(new FakeAutoStartService { State = AutoStartState.DisabledByWindows });

        Assert.True(vm.IsDisabledByWindows);
        Assert.False(vm.StartWithWindows);
    }

    [Fact]
    public void Does_not_write_when_disabled_by_windows()
    {
        var service = new FakeAutoStartService { State = AutoStartState.DisabledByWindows };
        var vm = new SettingsViewModel(service);

        vm.StartWithWindows = true; // UI disables the box; the guard also blocks it

        Assert.Equal(0, service.EnableCount);
    }

    [Fact]
    public void Done_closes_the_dialog()
    {
        var vm = new SettingsViewModel(new FakeAutoStartService());
        var closed = false;
        vm.CloseRequested += _ => closed = true;

        vm.DoneCommand.Execute(null);

        Assert.True(closed);
    }
}
