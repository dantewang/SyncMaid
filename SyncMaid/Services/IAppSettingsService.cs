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

    /// <summary>When true, the app launches with the main window hidden to the tray.
    /// Setting the value persists it; it takes effect on the next launch.</summary>
    bool StartMinimized { get; set; }

    /// <summary>The UI language as a BCP-47 tag, or null to follow the OS language.
    /// Setting the value persists it; applying it to the running UI is
    /// <c>Localizer.Apply</c>'s job (done at startup and by the Settings dialog).</summary>
    string? Language { get; set; }
}
