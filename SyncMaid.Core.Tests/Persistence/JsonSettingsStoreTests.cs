using System.Text;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Persistence;

public class JsonSettingsStoreTests
{
    private const string SettingsPath = @"C:\config\settings.json";

    private static JsonSettingsStore NewStore(InMemoryFileSystem fs) => new(fs, SettingsPath);

    [Fact]
    public void Load_returns_defaults_when_no_file_exists()
    {
        var settings = NewStore(new InMemoryFileSystem()).Load();

        Assert.False(settings.CloseToTray);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_blank()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(SettingsPath, Encoding.UTF8.GetBytes("   "));

        Assert.False(NewStore(fs).Load().CloseToTray);
    }

    [Fact]
    public void Round_trips_the_close_to_tray_flag()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);

        store.Save(new AppSettings(CloseToTray: true));

        Assert.True(store.Load().CloseToTray);
    }

    [Fact]
    public void An_interrupted_save_leaves_the_previous_file_intact()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(new AppSettings(CloseToTray: true));
        var before = fs.ReadAllBytes(SettingsPath);

        fs.FailWrites = true; // simulate a crash / power cut mid-write
        Assert.ThrowsAny<IOException>(() => store.Save(new AppSettings()));
        fs.FailWrites = false;

        Assert.Equal(before, fs.ReadAllBytes(SettingsPath));            // main file untouched
        Assert.DoesNotContain(fs.AllPaths, p => p.Contains(".tmp-"));   // temp cleaned up
        Assert.True(store.Load().CloseToTray);                          // previous value survives
    }

    [Fact]
    public void Load_falls_back_to_the_backup_when_the_main_file_is_corrupt()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(new AppSettings(CloseToTray: true));   // v1
        store.Save(new AppSettings(CloseToTray: false));  // v2; backup now holds v1

        fs.WriteAllBytes(SettingsPath, Encoding.UTF8.GetBytes("{ not valid json"));

        Assert.True(store.Load().CloseToTray); // main unreadable → recovered from the backup
    }
}
