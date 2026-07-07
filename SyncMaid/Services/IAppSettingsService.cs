namespace SyncMaid.Services;

/// <summary>
/// The runtime source of truth for application preferences. Loads once from the persisted
/// store and keeps the current values in memory so the close handler and the Settings dialog
/// agree; setting a property persists it immediately. A service (rather than passing the store
/// around) so a change made in the dialog is visible to the tray controller without a reload.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>When true, closing the main window hides it to the tray instead of exiting.
    /// Setting the value persists it.</summary>
    bool CloseToTray { get; set; }
}
