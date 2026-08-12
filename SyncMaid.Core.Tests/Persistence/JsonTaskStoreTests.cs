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
                // Non-default per-destination knobs on purpose: left at their defaults,
                // WhenWritingNull re-serializes them identically, so both the round-trip
                // and the byte-stability test would pass even if they were dropped on load.
                new Destination(
                    "Backup",
                    @"D:\backup",
                    [new PathFilter("2024"), new ExtensionFilter("jpg")],
                    SyncStrategy.AddOnly)
                {
                    VerifyContents = true,
                    DeleteMode = DeleteMode.Permanent,
                    MassDeleteThreshold = 0.25,
                },
            ]),
        new SyncTask(
            "Docs",
            @"C:\src\docs",
            new WatchTrigger(SettleSeconds: 45),
            [
                new Destination("Mirror", @"E:\mirror", [new AllFilesFilter()], SyncStrategy.Mirror),
                new Destination("Papers", @"E:\papers", [new ExtensionFilter("pdf")], SyncStrategy.AddOnly),
            ]),
        new SyncTask("Manual", @"C:\src\m", new ManualTrigger(), []),
        // A routing task: ordered Move destinations, the order being what decides where a
        // file two of them match ends up — so the saved order is load-bearing, not cosmetic.
        new SyncTask(
            "Sort downloads",
            @"C:\src\downloads",
            new WatchTrigger(SettleSeconds: 5),
            [
                new Destination("Books", @"E:\books", [new ExtensionFilter("pdf")], SyncStrategy.Move)
                {
                    FlattenStructure = true,
                    CollisionPolicy = FileNameCollisionPolicy.Suffix,
                },
                new Destination("To sort", @"E:\to-sort", [new AllFilesFilter()], SyncStrategy.Move),
            ]),
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

        var saved = SampleTasks();
        store.Save(saved);
        var loaded = store.Load();

        Assert.Equal(4, loaded.Count);

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

        // Where the files actually go, and the safety knobs guarding them. A dropped
        // destination path or a silently reset VerifyContents is invisible in the
        // filter/strategy assertions above.
        Assert.Equal(@"D:\backup", backup.LocalPath);
        Assert.Equal(@"D:\backup", Assert.IsType<LocalDestination>(backup.Target).Path);
        Assert.True(backup.VerifyContents);
        Assert.Equal(DeleteMode.Permanent, backup.DeleteMode);
        Assert.Equal(0.25, backup.MassDeleteThreshold);

        // Ids key the persisted per-destination status. They default to Guid.NewGuid(), so
        // a binding failure mints a fresh one instead of erroring — which orphans every
        // last-run result silently. Comparing against what was saved is the only way to
        // catch that; asserting "not empty" would pass on a freshly minted Guid.
        Assert.Equal(saved[0].Id, photos.Id);
        Assert.Equal(saved[0].Destinations[0].Id, backup.Id);

        // Multiple destinations and the remaining strategies/triggers.
        var docs = loaded[1];
        Assert.Equal(45, Assert.IsType<WatchTrigger>(docs.Trigger).SettleSeconds);
        Assert.Equal(2, docs.Destinations.Count);
        Assert.Equal(SyncStrategy.Mirror, docs.Destinations[0].Strategy);
        Assert.IsType<AllFilesFilter>(Assert.Single(docs.Destinations[0].Filters));
        Assert.Equal(SyncStrategy.AddOnly, docs.Destinations[1].Strategy);
        Assert.Equal(SyncTaskKind.Sync, docs.Kind);

        Assert.IsType<ManualTrigger>(loaded[2].Trigger);
        Assert.Empty(loaded[2].Destinations);

        // The routing task: kind, destination order, and the Move-only knobs.
        var sort = loaded[3];
        Assert.Equal(SyncTaskKind.Move, sort.Kind);
        Assert.Equal(["Books", "To sort"], sort.Destinations.Select(destination => destination.Name));
        Assert.True(sort.Destinations[0].FlattenStructure);
        Assert.Equal(FileNameCollisionPolicy.Suffix, sort.Destinations[0].CollisionPolicy);
        Assert.False(sort.Destinations[1].FlattenStructure);
        Assert.Equal(FileNameCollisionPolicy.Skip, sort.Destinations[1].CollisionPolicy);
    }

    // The kind is new; config written before it exists carries no field. Move was exclusive
    // then, so the destinations say what the task was — and the next save writes it down.
    [Fact]
    public void A_legacy_task_without_a_kind_derives_it_from_its_destinations()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes(
            """
            [{"Name":"Sort","SourcePath":"C:\\src","Trigger":{"kind":"manual"},
              "Destinations":[{"Name":"D","Target":{"kind":"local","Path":"D:\\d"},
                "Filters":[{"kind":"all"}],"Strategy":"Move"}]},
             {"Name":"Back up","SourcePath":"C:\\src2","Trigger":{"kind":"manual"},
              "Destinations":[{"Name":"D","Target":{"kind":"local","Path":"D:\\d2"},
                "Filters":[{"kind":"all"}],"Strategy":"Mirror"}]},
             {"Name":"Empty","SourcePath":"C:\\src3","Trigger":{"kind":"manual"},"Destinations":[]}]
            """));
        var store = NewStore(fs);

        var loaded = store.Load();

        Assert.Equal(
            SyncStrategy.Move,
            Assert.Single(loaded[0].Destinations).Strategy); // the value the kind derives from
        Assert.Equal(SyncTaskKind.Move, loaded[0].Kind);
        Assert.Equal(SyncTaskKind.Sync, loaded[1].Kind);
        // Nothing to derive from: a task with no destinations is a Sync task until the
        // user says otherwise, which is exactly why the field had to be persisted.
        Assert.Equal(SyncTaskKind.Sync, loaded[2].Kind);

        store.Save(loaded);
        var json = Encoding.UTF8.GetString(fs.ReadAllBytes(ConfigPath));
        Assert.Contains("\"Kind\": \"Move\"", json);
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
        Assert.Equal(SampleTasks().Count, store.Load().Count);                          // all tasks still load
    }

    // FailWrites above trips the very first write, before either rename. These cover the
    // two later failure points: a save is three steps (write temp, snapshot .bak, commit),
    // and a crash or a held file at *any* of them must leave the previous config loadable.
    [Fact]
    public void A_failure_committing_the_new_version_leaves_the_previous_one_in_place()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        var before = fs.ReadAllBytes(ConfigPath);

        // The commit rename is the last step; a reader or AV hold on the live file can
        // fail it. The primary must never have been moved aside to make room.
        fs.FailReplaceDestinationPath = ConfigPath;
        Assert.ThrowsAny<IOException>(() => store.Save([]));
        fs.FailReplaceDestinationPath = null;

        Assert.True(fs.FileExists(ConfigPath)); // never stopped existing
        Assert.Equal(before, fs.ReadAllBytes(ConfigPath));
        Assert.DoesNotContain(fs.AllPaths, p => p.Contains(".tmp-"));
        Assert.Equal(SampleTasks().Count, store.Load().Count);
    }

    [Fact]
    public void A_failure_snapshotting_the_backup_leaves_the_previous_version_in_place()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        var before = fs.ReadAllBytes(ConfigPath);

        fs.FailReplaceDestinationPath = ConfigPath + AtomicFile.BackupSuffix;
        Assert.ThrowsAny<IOException>(() => store.Save([]));
        fs.FailReplaceDestinationPath = null;

        Assert.Equal(before, fs.ReadAllBytes(ConfigPath));
        Assert.DoesNotContain(fs.AllPaths, p => p.Contains(".tmp-"));
        Assert.Equal(SampleTasks().Count, store.Load().Count);
    }

    // The state a power cut used to be able to produce, back when the backup step moved
    // the live file aside instead of copying it. Recovery has always worked; nothing
    // pinned it, so the fallback could have regressed unnoticed.
    [Fact]
    public void Load_recovers_when_the_main_file_is_missing_entirely_and_a_backup_exists()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());      // v1 (3 tasks)
        store.Save([SampleTasks()[0]]); // v2 (1 task); backup now holds v1

        fs.DeleteFile(ConfigPath);

        Assert.False(fs.FileExists(ConfigPath));
        Assert.Equal(SampleTasks().Count, store.Load().Count);
    }

    // "No tasks" and "your tasks are on disk but unreachable" produce the same empty list,
    // and the caller that acts on it goes on to Save. Without a way to tell them apart, a
    // transient lock (antivirus, a sync client, a roaming profile) turns into a config wipe
    // two saves later. Both files unreadable is the case no test covered.
    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("")]
    public void Load_reports_unreadable_when_neither_the_main_file_nor_the_backup_parses(string corrupt)
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]); // backup now exists

        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes(corrupt));
        fs.WriteAllBytes(ConfigPath + AtomicFile.BackupSuffix, Encoding.UTF8.GetBytes(corrupt));

        Assert.Empty(store.Load(out var unreadable));
        Assert.True(unreadable);
    }

    [Fact]
    public void Load_reports_unreadable_when_reading_both_files_throws()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]);

        // A held file, not a corrupt one: the config is perfectly intact on disk.
        fs.FailReadAllBytesPath = ConfigPath;
        fs.FailReadAllBytesPathFragment = AtomicFile.BackupSuffix;

        Assert.Empty(store.Load(out var unreadable));
        Assert.True(unreadable);
    }

    [Fact]
    public void A_first_run_with_no_config_at_all_is_not_unreadable()
    {
        // The genuinely-empty case must stay distinguishable, or a first run would refuse
        // to save anything.
        Assert.Empty(new JsonTaskStore(new InMemoryFileSystem(), ConfigPath).Load(out var unreadable));
        Assert.False(unreadable);
    }

    [Fact]
    public void A_corrupt_main_file_recovered_from_the_backup_is_not_unreadable()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]);
        fs.WriteAllBytes(ConfigPath, Encoding.UTF8.GetBytes("{ not valid json"));

        Assert.Equal(SampleTasks().Count, store.Load(out var unreadable).Count);
        Assert.False(unreadable); // recovery succeeded, so saving is safe
    }

    // Cleanup runs on the failure path, so a failing cleanup must not become the reported
    // cause — the caller needs the write failure that actually stopped the save.
    [Fact]
    public void A_cleanup_failure_does_not_replace_the_original_save_failure()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());

        fs.FailReplaceDestinationPath = ConfigPath;
        fs.FailDeletePathFragment = ".tmp-";

        var exception = Assert.ThrowsAny<IOException>(() => store.Save([]));

        Assert.Contains("Simulated rename failure", exception.Message);
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

        Assert.Equal(SampleTasks().Count, store.Load().Count); // main unreadable → recovered from the backup
    }

    [Fact]
    public void Load_falls_back_to_the_backup_when_the_main_file_read_throws()
    {
        var fs = new InMemoryFileSystem();
        var store = NewStore(fs);
        store.Save(SampleTasks());
        store.Save([SampleTasks()[0]]);
        fs.FailReadAllBytesPath = ConfigPath;

        Assert.Equal(SampleTasks().Count, store.Load().Count);
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

        Assert.Equal(SampleTasks().Count, store.Load().Count);
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
