using SyncMaid.Core.Model;

namespace SyncMaid.Core.Persistence;

/// <summary>
/// Persists the application's <see cref="AppSettings"/>. Mirrors the task/status stores:
/// a single JSON file written atomically, with a default returned when nothing is saved yet.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Loads the saved settings, or a default <see cref="AppSettings"/> if none exist.</summary>
    AppSettings Load();

    /// <summary>Writes the settings, replacing any previous version atomically.</summary>
    void Save(AppSettings settings);
}
