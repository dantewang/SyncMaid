using System.Text;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Persistence;

public class JsonTaskStoreTests
{
    private const string ConfigPath = @"C:\config\syncmaid.json";

    private static JsonTaskStore NewStore(InMemoryFileSystem fs) => new(fs, ConfigPath);

    private static List<SyncTask> SampleTasks() =>
    [
        new SyncTask(
            "Photos",
            @"C:\src\photos",
            new ScheduledTrigger("*/5 * * * *"),
            [
                new Destination(
                    "Backup",
                    @"D:\backup",
                    [new PathFilter("2024"), new ExtensionFilter("jpg")],
                    SyncStrategy.AddOnly),
            ]),
        new SyncTask(
            "Docs",
            @"C:\src\docs",
            new WatchTrigger(SettleSeconds: 45),
            [
                new Destination("Mirror", @"E:\mirror", [new AllFilesFilter()], SyncStrategy.Mirror),
                new Destination("Archive", @"E:\archive", [new ExtensionFilter("pdf")], SyncStrategy.Move),
            ]),
        new SyncTask("Manual", @"C:\src\m", new ManualTrigger(), []),
    ];

    [Fact]
    public void Load_returns_empty_when_no_file_exists()
    {
        Assert.Empty(NewStore(new InMemoryFileSystem()).Load());
    }

    [Fact]
    public void Load_returns_empty_when_file_is_blank()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes("   "));

        Assert.Empty(NewStore(fs).Load());
    }

    [Fact]
    public void Round_trip_preserves_task_scalars_triggers_filters_and_strategies()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);

        store.Save(SampleTasks());
        var loaded = store.Load();

        Assert.Equal(3, loaded.Count);

        // Scalars + polymorphic trigger with its payload.
        var photos = loaded[0];
        Assert.Equal("Photos", photos.Name);
        Assert.Equal(@"C:\src\photos", photos.SourcePath);
        var scheduled = Assert.IsType<ScheduledTrigger>(photos.Trigger);
        Assert.Equal("*/5 * * * *", scheduled.CronExpression);

        // Polymorphic filters round-trip to their concrete types with payloads, in order.
        var backup = Assert.Single(photos.Destinations);
        Assert.Equal(SyncStrategy.AddOnly, backup.Strategy);
        Assert.Collection(
            backup.Filters,
            f => Assert.Equal("2024", Assert.IsType<PathFilter>(f).Prefix),
            f => Assert.Equal("jpg", Assert.IsType<ExtensionFilter>(f).Extension));

        // Multiple destinations and the remaining strategies/triggers.
        var docs = loaded[1];
        Assert.Equal(45, Assert.IsType<WatchTrigger>(docs.Trigger).SettleSeconds);
        Assert.Equal(2, docs.Destinations.Count);
        Assert.Equal(SyncStrategy.Mirror, docs.Destinations[0].Strategy);
        Assert.IsType<AllFilesFilter>(Assert.Single(docs.Destinations[0].Filters));
        Assert.Equal(SyncStrategy.Move, docs.Destinations[1].Strategy);

        Assert.IsType<ManualTrigger>(loaded[2].Trigger);
        Assert.Empty(loaded[2].Destinations);
    }

    // Config written before the settle window existed carries a bare {"kind":"watch"}.
    [Fact]
    public void A_legacy_watch_trigger_without_settle_seconds_loads_with_the_default()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes(
            """
            [{"Name":"T","SourcePath":"C:\\src","Trigger":{"kind":"watch"},"Destinations":[]}]
            """));

        var trigger = Assert.IsType<WatchTrigger>(Assert.Single(NewStore(fs).Load()).Trigger);
        Assert.Equal(WatchTrigger.DefaultSettleSeconds, trigger.SettleSeconds);
    }

    [Fact]
    public void A_nested_composite_filter_round_trips()
    {
        // AllOf[AnyOf[path, ext], Not[ext]] — recursive polymorphism through source-gen.
        var expression = new AllOfFilter(
        [
            new AnyOfFilter([new PathFilter("docs"), new ExtensionFilter("jpg")]),
            new NotFilter(new ExtensionFilter("tmp")),
        ]);
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(
        [
            new SyncTask("T", @"C:\src", new ManualTrigger(),
                [new Destination("D", @"D:\d", [expression], SyncStrategy.Mirror)]),
        ]);

        var loaded = Assert.Single(Assert.Single(store.Load()).Destinations).Filters;

        Assert.Equal(expression, Assert.Single(loaded)); // records compare by value, recursively
    }

    [Fact]
    public void Slash_only_path_filter_remains_match_none_after_round_trip()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(
        [
            new SyncTask("T", @"C:\src", new ManualTrigger(),
                [new Destination("D", @"D:\d", [new PathFilter("/")], SyncStrategy.Mirror)]),
        ]);

        var loaded = Assert.IsType<PathFilter>(
            Assert.Single(Assert.Single(Assert.Single(store.Load()).Destinations).Filters));

        Assert.False(loaded.Matches("file.txt"));
        Assert.False(loaded.Matches("nested/file.txt"));
    }

    [Fact]
    public void Saved_form_is_stable_across_a_load_save_cycle()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);

        store.Save(SampleTasks());
        var first = fs.ReadAllBytes(ConfigPath);

        store.Save(store.Load());
        var second = fs.ReadAllBytes(ConfigPath);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Enums_persist_as_readable_strings_not_numbers()
    {
        var fs = new InMemoryFileSystem();
        NewStore(fs).Save(SampleTasks());

        var json = Encoding.UTF8.GetString(fs.ReadAllBytes(ConfigPath));

        Assert.Contains("AddOnly", json);   // UseStringEnumConverter
        Assert.Contains("\"kind\"", json);  // polymorphic discriminator
    }

    [Fact]
    public void An_interrupted_save_leaves_the_previous_file_intact()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        var before = fs.ReadAllBytes(ConfigPath);

        fs.FailWrites = true; // simulate a crash / power cut mid-write
        Assert.ThrowsAny<IOException>(() => store.Save([]));
        fs.FailWrites = false;

        Assert.Equal(before, fs.ReadAllBytes(ConfigPath));            // main file untouched
        Assert.DoesNotContain(fs.AllPaths, p => p.Contains(".tmp-")); // temp cleaned up
        Assert.Equal(3, store.Load().Count);                          // all tasks still load
    }

    [Fact]
    public void A_successful_save_leaves_no_temp_file()
    {
        var fs = new InMemoryFileSystem();
        NewStore(fs).Save(SampleTasks());

        Assert.DoesNotContain(fs.AllPaths, p => p.Contains(".tmp-"));
    }

    [Fact]
    public void Each_save_keeps_the_previous_version_as_a_backup()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);

        store.Save(SampleTasks());
        Assert.False(fs.FileExists(ConfigPath + AtomicFile.BackupSuffix)); // no backup on first save

        store.Save([SampleTasks()[0]]);
        Assert.True(fs.FileExists(ConfigPath + AtomicFile.BackupSuffix));  // previous version snapshotted
    }

    [Fact]
    public void Load_falls_back_to_the_backup_when_the_main_file_is_corrupt()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());          // v1 (3 tasks)
        store.Save([SampleTasks()[0]]);     // v2 (1 task); backup now holds v1

        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes("{ not valid json"));

        Assert.Equal(3, store.Load().Count); // main unreadable → recovered from the backup
    }

    [Fact]
    public void Load_falls_back_to_the_backup_when_the_main_file_read_throws()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]);
        fs.FailReadAllBytesPath = ConfigPath;

        Assert.Equal(3, store.Load().Count);
    }

    [Theory]
    [InlineData("[{\"Name\":\"docs\",\"SourcePath\":\"C:/docs\",\"Trigger\":{\"kind\":\"manual\"}}]")]
    [InlineData("[{\"Name\":\"docs\",\"SourcePath\":\"C:/docs\",\"Destinations\":[]}]")]
    public void Load_treats_missing_required_task_members_as_corrupt_and_uses_backup(string incompleteJson)
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]);
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes(incompleteJson));

        Assert.Equal(3, store.Load().Count);
    }

    [Theory]
    [InlineData("[{\"Name\":\"docs\",\"SourcePath\":\"C:/docs\",\"Trigger\":{\"kind\":\"manual\"}}]")]
    [InlineData("[{\"Name\":\"docs\",\"SourcePath\":\"C:/docs\",\"Destinations\":[]}]")]
    public void Load_returns_empty_when_required_task_members_and_backup_are_missing(string incompleteJson)
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes(incompleteJson));

        Assert.Empty(NewStore(fs).Load());
    }
}
