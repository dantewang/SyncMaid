using System;
using System.Runtime.Versioning;
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
/// Marked <see cref="SupportedOSPlatformAttribute"/> "windows" rather than guarding each method
/// with <see cref="OperatingSystem.IsWindows"/>: the composition root only constructs this
/// inside an <c>OperatingSystem.IsWindows()</c> branch (falling back to
/// <see cref="NoOpAutoStartService"/> elsewhere), which the platform-compatibility analyzer
/// recognizes — so the registry calls need no internal guards. See
/// docs/guide-os-specific-services.md.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "SyncMaid";

    /// <inheritdoc />
    public AutoStartState GetState()
    {
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
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
