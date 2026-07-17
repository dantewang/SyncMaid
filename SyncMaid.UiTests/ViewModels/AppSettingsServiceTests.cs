using System.Collections.Generic;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Services;

namespace SyncMaid.UiTests.ViewModels;

public class AppSettingsServiceTests
{
    [Fact]
    public void Loads_the_persisted_value_on_construction()
    {
        var store = new RecordingSettingsStore(new AppSettings(CloseToTray: true));

        var service = new AppSettingsService(store);

        Assert.True(service.CloseToTray);
    }

    [Fact]
    public void Changing_a_value_persists_it()
    {
        var store = new RecordingSettingsStore(new AppSettings());
        var service = new AppSettingsService(store);

        service.CloseToTray = true;

        Assert.True(service.CloseToTray);
        Assert.True(Assert.Single(store.Saved).CloseToTray);
    }

    [Fact]
    public void Setting_the_same_value_does_not_write()
    {
        var store = new RecordingSettingsStore(new AppSettings(CloseToTray: true));
        var service = new AppSettingsService(store);

        service.CloseToTray = true; // unchanged

        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Loads_the_persisted_start_minimized_value_on_construction()
    {
        var store = new RecordingSettingsStore(new AppSettings(StartMinimized: true));

        var service = new AppSettingsService(store);

        Assert.True(service.StartMinimized);
    }

    [Fact]
    public void Changing_start_minimized_persists_it()
    {
        var store = new RecordingSettingsStore(new AppSettings());
        var service = new AppSettingsService(store);

        service.StartMinimized = true;

        Assert.True(service.StartMinimized);
        Assert.True(Assert.Single(store.Saved).StartMinimized);
    }

    [Fact]
    public void Setting_the_same_start_minimized_value_does_not_write()
    {
        var store = new RecordingSettingsStore(new AppSettings(StartMinimized: true));
        var service = new AppSettingsService(store);

        service.StartMinimized = true; // unchanged

        Assert.Empty(store.Saved);
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        private readonly AppSettings _initial;
        public List<AppSettings> Saved { get; } = [];

        public RecordingSettingsStore(AppSettings initial) => _initial = initial;

        public AppSettings Load() => _initial;
        public void Save(AppSettings settings) => Saved.Add(settings);
    }
}
