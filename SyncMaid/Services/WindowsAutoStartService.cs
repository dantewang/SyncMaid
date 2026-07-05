using System;
using Microsoft.Win32;

namespace SyncMaid.Services;

/// <summary>
/// Autostart via the per-user Run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>) — the standard,
/// least-AV-suspicious mechanism for an unpackaged Win32 app: per-user (no elevation) and
/// fully visible/controllable in Task Manager → Startup. The Run value is the single source
/// of truth; no settings file is involved. See docs/guide-settings-autostart.md.
/// </summary>
/// <remarks>
/// The registry calls are guarded by <see cref="OperatingSystem.IsWindows"/> — the app only
/// ever runs on Windows, but the guard satisfies the platform-compatibility analyzer and
/// makes the service a harmless no-op anywhere else.
/// </remarks>
public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "SyncMaid";

    /// <inheritdoc />
    public AutoStartState GetState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return AutoStartState.Disabled;
        }

        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (run?.GetValue(ValueName) is not string value || string.IsNullOrEmpty(value))
        {
            return AutoStartState.Disabled;
        }

        // Task Manager's "disable" doesn't delete the Run value; it writes a status blob
        // here whose first byte is odd when the entry is disabled. We read it (never write
        // it — that override is exactly what AV heuristics dislike).
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        if (approved?.GetValue(ValueName) is byte[] flag && flag.Length > 0 && (flag[0] & 1) == 1)
        {
            return AutoStartState.DisabledByWindows;
        }

        return AutoStartState.Enabled;
    }

    /// <inheritdoc />
    public void Enable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        // Quote the path so a space in the install location doesn't break the command.
        run.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
