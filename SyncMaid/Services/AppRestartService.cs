using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace SyncMaid.Services;

/// <summary>
/// Relaunches the current executable and shuts the running instance down. Resolves the desktop
/// lifetime from <see cref="Application.Current"/> at call time (the lifetime does not exist yet
/// when the DI graph is built). Single-instance is not enforced, so the two overlap briefly
/// during shutdown — acceptable for a restart.
/// </summary>
public sealed class AppRestartService : IAppRestartService
{
    public void Restart()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executable))
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
