using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SyncMaid.Services;

/// <summary>
/// Finds the window that dialogs and pickers should attach to. Centralized here so the
/// service implementations don't each reach into the application lifetime, and so view
/// models never need a <see cref="Window"/> reference.
/// </summary>
internal static class WindowLocator
{
    /// <summary>
    /// The currently active window (e.g. an open editor dialog), falling back to the
    /// main window. Null only before the UI is up.
    /// </summary>
    public static Window? Active()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
        }

        return null;
    }
}
