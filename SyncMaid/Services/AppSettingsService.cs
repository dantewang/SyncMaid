using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;

namespace SyncMaid.Services;

/// <summary>
/// In-memory <see cref="IAppSettingsService"/> backed by an <see cref="ISettingsStore"/>.
/// Loads the persisted settings on construction and writes back on each change. A save is
/// skipped when the value is unchanged, so re-seeding the UI never triggers a redundant write.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly ISettingsStore _store;
    private AppSettings _current;

    public AppSettingsService(ISettingsStore store)
    {
        _store = store;
        _current = store.Load();
    }

    public bool CloseToTray
    {
        get => _current.CloseToTray;
        set
        {
            if (value == _current.CloseToTray)
            {
                return;
            }

            _current = _current with { CloseToTray = value };
            _store.Save(_current);
        }
    }

    public bool StartMinimized
    {
        get => _current.StartMinimized;
        set
        {
            if (value == _current.StartMinimized)
            {
                return;
            }

            _current = _current with { StartMinimized = value };
            _store.Save(_current);
        }
    }

    public string? Language
    {
        get => _current.Language;
        set
        {
            if (value == _current.Language)
            {
                return;
            }

            _current = _current with { Language = value };
            _store.Save(_current);
        }
    }
}
