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
            new WatchTrigger(),
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
        Assert.IsType<WatchTrigger>(docs.Trigger);
        Assert.Equal(2, docs.Destinations.Count);
        Assert.Equal(SyncStrategy.Mirror, docs.Destinations[0].Strategy);
        Assert.IsType<AllFilesFilter>(Assert.Single(docs.Destinations[0].Filters));
        Assert.Equal(SyncStrategy.Move, docs.Destinations[1].Strategy);

        Assert.IsType<ManualTrigger>(loaded[2].Trigger);
        Assert.Empty(loaded[2].Destinations);
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
}
