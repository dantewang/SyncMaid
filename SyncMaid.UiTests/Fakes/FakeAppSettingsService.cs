using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>In-memory <see cref="IAppSettingsService"/> for view-model and tray tests.</summary>
public sealed class FakeAppSettingsService : IAppSettingsService
{
    public bool CloseToTray { get; set; }

    public bool StartMinimized { get; set; }

    public string? Language { get; set; }
}
