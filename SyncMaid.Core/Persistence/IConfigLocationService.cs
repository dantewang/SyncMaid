namespace SyncMaid.Core.Persistence;

/// <summary>Where SyncMaid keeps its config/data files.</summary>
public enum ConfigLocationMode
{
    /// <summary>The per-user app-data folder (<c>%APPDATA%\SyncMaid</c>). The default.</summary>
    AppData,

    /// <summary>A <c>Data</c> folder beside the executable, for portable / USB-stick installs
    /// where everything travels with the app and nothing is left in the user profile.</summary>
    Portable,
}

/// <summary>
/// Resolves where config/data lives and moves it between locations. The mode is decided by a
/// marker file beside the executable (not by a setting inside the config folder — that would be
/// circular), so a portable install is fully self-contained. Switching migrates the existing
/// files; the caller restarts the app afterwards since paths are wired at startup.
/// </summary>
public interface IConfigLocationService
{
    /// <summary>The mode currently in effect.</summary>
    ConfigLocationMode CurrentMode { get; }

    /// <summary>The absolute config directory currently in use.</summary>
    string CurrentDirectory { get; }

    /// <summary>The directory a given mode would use (to show the path in the UI).</summary>
    string DirectoryFor(ConfigLocationMode mode);

    /// <summary>Whether <paramref name="mode"/>'s target directory can be written to (portable
    /// next to a read-only install directory cannot).</summary>
    bool CanUse(ConfigLocationMode mode);

    /// <summary>
    /// Migrates the config files to <paramref name="mode"/>'s directory and sets/clears the
    /// marker. Copies are verified before the source is removed, and the marker is flipped only
    /// after every file has moved, so a failure leaves the current location intact. Returns true
    /// on success. Does not restart — the caller relaunches so the new paths take effect.
    /// </summary>
    bool SwitchTo(ConfigLocationMode mode);
}
