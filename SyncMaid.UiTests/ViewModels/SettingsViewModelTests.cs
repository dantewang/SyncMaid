using System.Linq;
using Avalonia.Headless.XUnit;
using SyncMaid.Core.Persistence;
using SyncMaid.Lang;
using SyncMaid.Services;
using SyncMaid.UiTests.Fakes;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class SettingsViewModelTests
{
    private static SettingsViewModel New(
        FakeAutoStartService? autoStart = null,
        FakeAppSettingsService? settings = null,
        FakeConfigLocationService? configLocation = null,
        FakeAppRestartService? restart = null) =>
        new(
            autoStart ?? new FakeAutoStartService(),
            settings ?? new FakeAppSettingsService(),
            configLocation ?? new FakeConfigLocationService(),
            restart ?? new FakeAppRestartService());

    [Fact]
    public void Reflects_enabled_state_without_writing_on_load()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Enabled };

        var vm = New(service);

        Assert.True(vm.StartWithWindows);
        Assert.False(vm.IsDisabledByWindows);
        Assert.Equal(0, service.EnableCount);   // seeding state must not write to the registry
        Assert.Equal(0, service.DisableCount);
    }

    [Fact]
    public void Reflects_disabled_state()
    {
        var vm = New(new FakeAutoStartService { State = AutoStartState.Disabled });

        Assert.False(vm.StartWithWindows);
        Assert.False(vm.IsDisabledByWindows);
    }

    [Fact]
    public void Toggling_on_enables_autostart_once()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Disabled };
        var vm = New(service);

        vm.StartWithWindows = true;

        Assert.Equal(1, service.EnableCount);
        Assert.Equal(0, service.DisableCount);
    }

    [Fact]
    public void Toggling_off_disables_autostart_once()
    {
        var service = new FakeAutoStartService { State = AutoStartState.Enabled };
        var vm = New(service);

        vm.StartWithWindows = false;

        Assert.Equal(1, service.DisableCount);
        Assert.Equal(0, service.EnableCount);
    }

    [Fact]
    public void Disabled_by_windows_shows_the_notice_and_reads_as_off()
    {
        var vm = New(new FakeAutoStartService { State = AutoStartState.DisabledByWindows });

        Assert.True(vm.IsDisabledByWindows);
        Assert.False(vm.StartWithWindows);
    }

    [Fact]
    public void Does_not_write_when_disabled_by_windows()
    {
        var service = new FakeAutoStartService { State = AutoStartState.DisabledByWindows };
        var vm = New(service);

        vm.StartWithWindows = true; // UI disables the box; the guard also blocks it

        Assert.Equal(0, service.EnableCount);
    }

    [Fact]
    public void Reflects_the_stored_close_to_tray_value_without_writing_on_load()
    {
        var settings = new FakeAppSettingsService { CloseToTray = true };

        var vm = New(settings: settings);

        Assert.True(vm.CloseToTray);
        Assert.True(settings.CloseToTray); // seeding must not toggle it back
    }

    [Fact]
    public void Toggling_close_to_tray_updates_the_setting()
    {
        var settings = new FakeAppSettingsService { CloseToTray = false };
        var vm = New(settings: settings);

        vm.CloseToTray = true;

        Assert.True(settings.CloseToTray);
    }

    [Fact]
    public void Seeds_the_stored_language_without_writing_on_load()
    {
        var settings = new FakeAppSettingsService { Language = "ja" };

        var vm = New(settings: settings);

        Assert.Equal("ja", vm.SelectedLanguage.Tag);
        Assert.Equal("ja", settings.Language); // seeding must not rewrite it
    }

    [Fact]
    public void An_unknown_stored_language_shows_as_system_default()
    {
        var vm = New(settings: new FakeAppSettingsService { Language = "xx-Nope" });

        Assert.Null(vm.SelectedLanguage.Tag);
    }

    // [AvaloniaFact], not [Fact]: switching the culture notifies every live view model —
    // including UI-bound ones from earlier headless tests — so it must run on the UI
    // thread, exactly as it does in production.
    [AvaloniaFact]
    public void Picking_a_language_persists_it_and_switches_the_ui_culture()
    {
        var settings = new FakeAppSettingsService();
        var vm = New(settings: settings);
        try
        {
            vm.SelectedLanguage = vm.Languages.Single(option => option.Tag == "zh-Hans");

            Assert.Equal("zh-Hans", settings.Language);
            Assert.Equal("运行全部", Strings.Main_RunAll); // strings now resolve in the picked language
        }
        finally
        {
            Localizer.Instance.Apply("en");
        }
    }

    [Fact]
    public void Storage_section_reflects_the_current_location()
    {
        var vm = New(configLocation: new FakeConfigLocationService { CurrentMode = ConfigLocationMode.Portable });

        Assert.Contains("portable", vm.StorageModeText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app data", vm.SwitchStorageText, System.StringComparison.OrdinalIgnoreCase); // switches to the other
        Assert.False(vm.HasStorageError);
    }

    [Fact]
    public void Switching_storage_migrates_and_restarts()
    {
        var location = new FakeConfigLocationService { CurrentMode = ConfigLocationMode.AppData };
        var restart = new FakeAppRestartService();
        var vm = New(configLocation: location, restart: restart);

        vm.SwitchStorageCommand.Execute(null);

        Assert.Equal(ConfigLocationMode.Portable, location.SwitchedTo);   // moved to the other mode
        Assert.Equal(1, restart.RestartCount);                            // and relaunched
        Assert.False(vm.HasStorageError);
    }

    [Fact]
    public void Switching_to_an_unwritable_target_is_refused_without_restarting()
    {
        var location = new FakeConfigLocationService { CanUseResult = false };
        var restart = new FakeAppRestartService();
        var vm = New(configLocation: location, restart: restart);

        vm.SwitchStorageCommand.Execute(null);

        Assert.Null(location.SwitchedTo);        // never attempted the move
        Assert.Equal(0, restart.RestartCount);   // no restart
        Assert.True(vm.HasStorageError);
    }

    [Fact]
    public void A_failed_migration_shows_an_error_and_does_not_restart()
    {
        var location = new FakeConfigLocationService { SwitchResult = false };
        var restart = new FakeAppRestartService();
        var vm = New(configLocation: location, restart: restart);

        vm.SwitchStorageCommand.Execute(null);

        Assert.Equal(0, restart.RestartCount);
        Assert.True(vm.HasStorageError);
    }

    [Fact]
    public void Enter_closes_the_settings_dialog()
    {
        var vm = New();
        var closed = false;
        vm.CloseRequested += _ => closed = true;

        Assert.True(vm.RequestAccept());
        Assert.True(closed);
    }

    [Fact]
    public void Done_closes_the_dialog()
    {
        var vm = New();
        var closed = false;
        vm.CloseRequested += _ => closed = true;

        vm.DoneCommand.Execute(null);

        Assert.True(closed);
    }
}
