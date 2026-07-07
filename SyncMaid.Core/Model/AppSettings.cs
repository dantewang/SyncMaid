namespace SyncMaid.Core.Model;

/// <summary>
/// User-facing application preferences (as opposed to task/destination configuration).
/// Persisted as <c>settings.json</c> alongside <c>tasks.json</c>. A record so it is
/// immutable and cheap to copy with <c>with</c> when a single field changes.
/// </summary>
public sealed record AppSettings(
    /// <summary>When true, closing the main window hides it to the system tray and leaves
    /// SyncMaid running (triggers keep firing) rather than exiting the app.</summary>
    bool CloseToTray = false);
