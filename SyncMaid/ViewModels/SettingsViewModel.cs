using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

/// <summary>
/// The Settings dialog. For now a single option — start SyncMaid with Windows — applied
/// immediately on toggle (a registry write; no separate save step). The dialog result is
/// unused; it just closes.
/// </summary>
public partial class SettingsViewModel : DialogViewModel<bool>
{
    private readonly IAutoStartService _autoStart;

    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>True when Windows Task Manager has switched autostart off; the checkbox is
    /// disabled and a notice points the user there.</summary>
    [ObservableProperty]
    private bool _isDisabledByWindows;

    public SettingsViewModel(IAutoStartService autoStart)
    {
        _autoStart = autoStart;
        var state = autoStart.GetState();
        // Set the backing fields directly so seeding the initial state does not fire the
        // toggle handler (which would perform a redundant registry write).
        _startWithWindows = state == AutoStartState.Enabled;
        _isDisabledByWindows = state == AutoStartState.DisabledByWindows;
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

    [RelayCommand]
    private void Done() => Close(true);
}
