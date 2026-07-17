namespace SyncMaid.Core.Model;

/// <summary>
/// User-facing application preferences (as opposed to task/destination configuration).
/// Persisted as <c>settings.json</c> alongside <c>tasks.json</c>. A record so it is
/// immutable and cheap to copy with <c>with</c> when a single field changes.
/// </summary>
public sealed record AppSettings(
    /// <summary>When true, closing the main window hides it to the system tray and leaves
    /// SyncMaid running (triggers keep firing) rather than exiting the app.</summary>
    bool CloseToTray = false,
    /// <summary>When true, the app launches with the main window hidden — only the tray icon
    /// is present — on every launch, independent of how the app was started (manually or via
    /// autostart). Triggers run as usual; the tray icon brings the window up on demand.</summary>
    bool StartMinimized = false,
    /// <summary>UI language as a BCP-47 tag (e.g. "zh-Hans"); null means follow the OS
    /// language. A tag, not display text — how it renders is the UI layer's concern.</summary>
    string? Language = null);
