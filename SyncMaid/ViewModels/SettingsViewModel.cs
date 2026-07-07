using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// The Settings dialog. Each option is applied immediately on toggle (a registry write for
/// autostart; a persisted setting for close-to-tray) — there is no separate save step. The
/// dialog result is unused; it just closes.
/// </summary>
public partial class SettingsViewModel : DialogViewModel<bool>
{
    private readonly IAutoStartService _autoStart;
    private readonly IAppSettingsService _appSettings;

    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>True when Windows Task Manager has switched autostart off; the checkbox is
    /// disabled and a notice points the user there.</summary>
    [ObservableProperty]
    private bool _isDisabledByWindows;

    /// <summary>When on, closing the main window hides it to the tray instead of exiting.</summary>
    [ObservableProperty]
    private bool _closeToTray;

    public SettingsViewModel(IAutoStartService autoStart, IAppSettingsService appSettings)
    {
        _autoStart = autoStart;
        _appSettings = appSettings;
        var state = autoStart.GetState();
        // Set the backing fields directly so seeding the initial state does not fire the
        // toggle handlers (which would perform a redundant registry / settings write).
        _startWithWindows = state == AutoStartState.Enabled;
        _isDisabledByWindows = state == AutoStartState.DisabledByWindows;
        _closeToTray = appSettings.CloseToTray;
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

    [RelayCommand]
    private void Done() => Close(true);
}
