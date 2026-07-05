namespace SyncMaid.Services;

/// <summary>Whether SyncMaid is configured to launch at Windows sign-in.</summary>
public enum AutoStartState
{
    /// <summary>Not registered to start with Windows.</summary>
    Disabled,

    /// <summary>Registered and active.</summary>
    Enabled,

    /// <summary>Registered, but the user switched it off in Windows Task Manager's Startup
    /// list. We honour that and don't silently re-enable — the UI tells the user to flip it
    /// back there.</summary>
    DisabledByWindows,
}

/// <summary>
/// Manages launching SyncMaid when the user signs in to Windows. Platform-neutral contract
/// so view models depend on it and tests substitute a fake.
/// </summary>
public interface IAutoStartService
{
    /// <summary>Current autostart state.</summary>
    AutoStartState GetState();

    /// <summary>Registers the current executable to start with Windows.</summary>
    void Enable();

    /// <summary>Removes the autostart registration (no-op if absent).</summary>
    void Disable();
}
