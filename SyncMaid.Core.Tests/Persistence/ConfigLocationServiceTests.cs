using System.Text;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Persistence;

public class ConfigLocationServiceTests
{
    private const string AppData = @"C:\Users\me\AppData\Roaming\SyncMaid";
    private const string Portable = @"C:\app\Data";
    private const string Marker = @"C:\app\portable.marker";

    private static ConfigLocationService New(InMemoryFileSystem fs) => new(fs, AppData, Portable, Marker);

    [Fact]
    public void Defaults_to_app_data_when_no_marker_exists()
    {
        var service = New(new InMemoryFileSystem());

        Assert.Equal(ConfigLocationMode.AppData, service.CurrentMode);
        Assert.Equal(AppData, service.CurrentDirectory);
    }

    [Fact]
    public void Is_portable_when_the_marker_is_present()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(Marker, [1]);

        var service = New(fs);

        Assert.Equal(ConfigLocationMode.Portable, service.CurrentMode);
        Assert.Equal(Portable, service.CurrentDirectory);
    }

    [Fact]
    public void Switching_to_portable_moves_the_files_and_writes_the_marker()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes($@"{AppData}\tasks.json", Encoding.UTF8.GetBytes("[tasks]"));
        fs.WriteAllBytes($@"{AppData}\status.json", Encoding.UTF8.GetBytes("[status]"));

        var service = New(fs);
        var ok = service.SwitchTo(ConfigLocationMode.Portable);

        Assert.True(ok);
        Assert.Equal(ConfigLocationMode.Portable, service.CurrentMode);
        Assert.True(fs.FileExists(Marker));
        // Copied to the portable dir…
        Assert.Equal("[tasks]", Encoding.UTF8.GetString(fs.ReadAllBytes($"{Portable}/tasks.json")));
        Assert.Equal("[status]", Encoding.UTF8.GetString(fs.ReadAllBytes($"{Portable}/status.json")));
        // …and removed from the old location.
        Assert.False(fs.FileExists($@"{AppData}\tasks.json"));
        Assert.False(fs.FileExists($@"{AppData}\status.json"));
    }

    [Fact]
    public void Switching_back_to_app_data_moves_the_files_and_removes_the_marker()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(Marker, [1]);
        fs.WriteAllBytes($"{Portable}/tasks.json", Encoding.UTF8.GetBytes("[tasks]"));

        var service = New(fs);
        var ok = service.SwitchTo(ConfigLocationMode.AppData);

        Assert.True(ok);
        Assert.Equal(ConfigLocationMode.AppData, service.CurrentMode);
        Assert.False(fs.FileExists(Marker));
        Assert.Equal("[tasks]", Encoding.UTF8.GetString(fs.ReadAllBytes($"{AppData}/tasks.json")));
        Assert.False(fs.FileExists($"{Portable}/tasks.json"));
    }

    [Fact]
    public void Also_migrates_the_backup_files()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes($@"{AppData}\tasks.json", [1]);
        fs.WriteAllBytes($@"{AppData}\tasks.json{AtomicFile.BackupSuffix}", [2]);

        New(fs).SwitchTo(ConfigLocationMode.Portable);

        Assert.True(fs.FileExists($"{Portable}/tasks.json{AtomicFile.BackupSuffix}"));
        Assert.False(fs.FileExists($@"{AppData}\tasks.json{AtomicFile.BackupSuffix}"));
    }

    [Fact]
    public void Switching_to_the_same_mode_is_a_no_op()
    {
        var fs = new InMemoryFileSystem();

        Assert.True(New(fs).SwitchTo(ConfigLocationMode.AppData));
        Assert.False(fs.FileExists(Marker)); // nothing created
    }

    [Fact]
    public void An_unwritable_target_is_refused_and_leaves_the_source_intact()
    {
        var fs = new InMemoryFileSystem { FailWrites = true }; // simulate a read-only target
        fs.AddFile(@"C:\Users\me\AppData\Roaming\SyncMaid\tasks.json", "[tasks]");

        var service = New(fs);

        Assert.False(service.CanUse(ConfigLocationMode.Portable));
        Assert.False(service.SwitchTo(ConfigLocationMode.Portable));
        Assert.Equal(ConfigLocationMode.AppData, service.CurrentMode);           // stayed put
        Assert.True(fs.FileExists(@"C:\Users\me\AppData\Roaming\SyncMaid\tasks.json")); // source intact
        Assert.False(fs.FileExists(Marker));
    }

    [Fact]
    public void Marker_write_failure_leaves_the_active_location_loadable()
    {
        var sourceFile = $@"{AppData}\tasks.json";
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("[tasks]"));
        fs.FailWriteAllBytesPath = Marker;

        var service = New(fs);
        var switched = service.SwitchTo(ConfigLocationMode.Portable);

        Assert.False(switched);
        Assert.Equal(ConfigLocationMode.AppData, service.CurrentMode);
        Assert.Equal("[tasks]", Encoding.UTF8.GetString(fs.ReadAllBytes(sourceFile)));
        Assert.False(fs.FileExists(Marker));
    }

    [Fact]
    public void Source_cleanup_failure_does_not_roll_back_a_successful_switch()
    {
        var sourceFile = $@"{AppData}\tasks.json";
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("[tasks]"));
        fs.FailDeletePath = sourceFile;

        var service = New(fs);
        var switched = service.SwitchTo(ConfigLocationMode.Portable);

        Assert.True(switched);
        Assert.Equal(ConfigLocationMode.Portable, service.CurrentMode);
        Assert.True(fs.FileExists(Marker));
        Assert.Equal("[tasks]", Encoding.UTF8.GetString(fs.ReadAllBytes($"{Portable}/tasks.json")));
        Assert.True(fs.FileExists(sourceFile));
    }

    [Fact]
    public void Unexpected_cleanup_failure_cannot_report_failure_after_the_marker_commits()
    {
        var sourceFile = $@"{AppData}\tasks.json";
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes("[tasks]"));
        fs.FailDeletePath = sourceFile;
        fs.DeleteFailure = () => new InvalidOperationException("unexpected cleanup failure");

        var service = New(fs);
        var switched = service.SwitchTo(ConfigLocationMode.Portable);

        Assert.True(switched);
        Assert.Equal(ConfigLocationMode.Portable, service.CurrentMode);
        Assert.True(fs.FileExists(Marker));
        Assert.Equal("[tasks]", Encoding.UTF8.GetString(fs.ReadAllBytes($"{Portable}/tasks.json")));
        Assert.True(fs.FileExists(sourceFile));
    }
}
