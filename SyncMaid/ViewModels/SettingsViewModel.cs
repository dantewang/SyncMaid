using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Persistence;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// The Settings dialog. Each option is applied immediately on toggle (a registry write for
/// autostart; a persisted setting for close-to-tray) — there is no separate save step. The
/// storage-location switch is the exception: it migrates the config files and restarts the app.
/// The dialog result is unused; it just closes.
/// </summary>
public partial class SettingsViewModel : DialogViewModel<bool>
{
    private readonly IAutoStartService _autoStart;
    private readonly IAppSettingsService _appSettings;
    private readonly IConfigLocationService _configLocation;
    private readonly IAppRestartService _restart;

    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>True when Windows Task Manager has switched autostart off; the checkbox is
    /// disabled and a notice points the user there.</summary>
    [ObservableProperty]
    private bool _isDisabledByWindows;

    /// <summary>When on, closing the main window hides it to the tray instead of exiting.</summary>
    [ObservableProperty]
    private bool _closeToTray;

    /// <summary>The picked UI language; switching applies immediately, like every other
    /// option here.</summary>
    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    /// <summary>Set when a storage switch is refused (e.g. an unwritable target) or fails; shown
    /// as an inline notice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorageError))]
    private string? _storageError;

    public SettingsViewModel(
        IAutoStartService autoStart,
        IAppSettingsService appSettings,
        IConfigLocationService configLocation,
        IAppRestartService restart)
    {
        _autoStart = autoStart;
        _appSettings = appSettings;
        _configLocation = configLocation;
        _restart = restart;
        var state = autoStart.GetState();
        // Set the backing fields directly so seeding the initial state does not fire the
        // toggle handlers (which would perform a redundant registry / settings write).
        _startWithWindows = state == AutoStartState.Enabled;
        _isDisabledByWindows = state == AutoStartState.DisabledByWindows;
        _closeToTray = appSettings.CloseToTray;
        // An unrecognized persisted tag (hand-edited settings.json) shows as system default.
        _selectedLanguage = Languages.FirstOrDefault(option => option.Tag == appSettings.Language)
                            ?? Languages[0];
    }

    /// <summary>The pickable UI languages: the OS default plus every built-in translation.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(null, Strings.Settings_SystemDefault),
        new("en", "English"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("ja", "日本語"),
    ];

    /// <summary>The mode the target of a switch would be — the opposite of the current one.</summary>
    private ConfigLocationMode OtherMode =>
        _configLocation.CurrentMode == ConfigLocationMode.Portable
            ? ConfigLocationMode.AppData
            : ConfigLocationMode.Portable;

    /// <summary>Human label for where data currently lives.</summary>
    public string StorageModeText =>
        _configLocation.CurrentMode == ConfigLocationMode.Portable
            ? "Next to the app (portable)"
            : "App data folder";

    /// <summary>The absolute folder currently in use, shown read-only.</summary>
    public string StoragePath => _configLocation.CurrentDirectory;

    /// <summary>Label for the switch button, naming the destination mode.</summary>
    public string SwitchStorageText =>
        OtherMode == ConfigLocationMode.Portable
            ? "Move data next to the app (portable)"
            : "Move data to the app data folder";

    /// <summary>True when a storage notice should be shown.</summary>
    public bool HasStorageError => !string.IsNullOrEmpty(StorageError);

    // Migrates the config files to the other location and restarts so the new paths take
    // effect. Refuses (with a notice, no restart) if the target is not writable or the move
    // fails, leaving the current location intact.
    [RelayCommand]
    private void SwitchStorage()
    {
        StorageError = null;
        var target = OtherMode;

        if (!_configLocation.CanUse(target))
        {
            StorageError = $"Can't use {_configLocation.DirectoryFor(target)} — the folder is not writable " +
                           "(a portable install under Program Files needs a writable location).";
            return;
        }

        if (_configLocation.SwitchTo(target))
        {
            _restart.Restart();
        }
        else
        {
            StorageError = "Couldn't move your data — it was left where it is.";
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isDisabledByWindows)
        {
            return; // Guarded in the UI too; belt and suspenders.
        }

        if (value)
        {
            _autoStart.Enable();
        }
        else
        {
            _autoStart.Disable();
        }
    }

    partial void OnCloseToTrayChanged(bool value) => _appSettings.CloseToTray = value;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        _appSettings.Language = value.Tag;
        Localizer.Instance.Apply(value.Tag);
    }

    [RelayCommand]
    private void Done() => Close(true);

    /// <summary>Enter closes the settings dialog (all options apply live).</summary>
    public override bool RequestAccept()
    {
        Done();
        return true;
    }
}
