using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Sync;

public class SyncPlannerTests
{
    private const string SourceRoot = @"S:\src";
    private const string DestRoot = @"D:\dst";

    private static Destination Dest(SyncStrategy strategy) =>
        new("dest", DestRoot, new FilterRule[] { new AllFilesFilter() }, strategy);

    private static IReadOnlyList<SyncOperation> PlanFor(
        InMemoryFileSystem fs, SyncStrategy strategy, params string[] filtered) =>
        SyncPlanner.Plan(
            fs, SourceRoot, new LocalDestinationProvider(fs, DestRoot), Dest(strategy),
            filtered, SourceDirectories(fs)).Operations;

    private static IReadOnlyList<string> SourceDirectories(InMemoryFileSystem fs)
    {
        try
        {
            return fs.EnumerateDirectories(SourceRoot).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    [Fact]
    public void AddOnly_copies_only_new_and_changed_files()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\new.txt", "new", t);
        fs.AddFile(@"S:\src\changed.txt", "v2-longer", t);          // size differs from dest
        fs.AddFile(@"S:\src\same.txt", "same", t);
        fs.AddFile(@"D:\dst\changed.txt", "v1", t);
        fs.AddFile(@"D:\dst\same.txt", "same", t);                  // identical stamp -> skip

        var ops = PlanFor(fs, SyncStrategy.AddOnly, "new.txt", "changed.txt", "same.txt");

        var copied = ops.OfType<CopyOperation>().Select(o => o.RelativePath).OrderBy(p => p).ToList();
        Assert.Equal(new[] { "changed.txt", "new.txt" }, copied);
        Assert.Empty(ops.OfType<DeleteOperation>()); // AddOnly never deletes
    }

    [Fact]
    public void Mirror_copies_changes_and_deletes_extras()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);   // identical -> no copy
        fs.AddFile(@"D:\dst\orphan.txt", "stale", t); // not in source -> delete

        var ops = PlanFor(fs, SyncStrategy.Mirror, "keep.txt");

        Assert.Empty(ops.OfType<CopyOperation>());
        var deleted = ops.OfType<DeleteOperation>().Select(o => o.RelativePath).ToList();
        Assert.Equal(new[] { "orphan.txt" }, deleted);
    }

    [Fact]
    public void Mirror_removes_destination_directories_no_longer_in_the_source_deepest_first_after_the_file_deletes()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep\photo.png", "keep", t);
        fs.AddFile(@"D:\dst\keep\photo.png", "keep", t);      // identical -> kept
        fs.AddFile(@"D:\dst\gone\sub\a.txt", "stale", t);     // whole tree orphaned
        fs.AddFile(@"D:\dst\gone\b.txt", "stale", t);

        var ops = PlanFor(fs, SyncStrategy.Mirror, "keep/photo.png").ToList();

        var directories = ops.OfType<DeleteDirectoryOperation>().Select(o => o.RelativePath).ToList();
        Assert.Equal(new[] { "gone/sub", "gone" }, directories); // children before parents
        Assert.True(
            ops.FindIndex(o => o is DeleteDirectoryOperation) > ops.FindLastIndex(o => o is DeleteOperation),
            "directory removals must come after every file deletion");
    }

    // The contract: a destination directory lives exactly as long as its source
    // directory — a source folder emptied of files but kept must stay mirrored.
    [Fact]
    public void Mirror_keeps_a_destination_directory_whose_source_directory_is_empty_but_present()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.EnsureDirectory(@"S:\src\a");                      // exists at source, holds no files
        fs.AddFile(@"D:\dst\a\orphan.txt", "stale", t);

        var ops = PlanFor(fs, SyncStrategy.Mirror);

        Assert.Equal("a/orphan.txt", Assert.Single(ops.OfType<DeleteOperation>()).RelativePath);
        Assert.Empty(ops.OfType<DeleteDirectoryOperation>());
    }

    [Fact]
    public void Mirror_creates_missing_source_directories_parents_first_even_when_empty()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);
        fs.EnsureDirectory(@"S:\src\empty\nested");           // no files anywhere beneath

        var ops = PlanFor(fs, SyncStrategy.Mirror, "keep.txt");

        var created = ops.OfType<CreateDirectoryOperation>().Select(o => o.RelativePath).ToList();
        Assert.Equal(new[] { "empty", "empty/nested" }, created); // parents before children
    }

    [Fact]
    public void Mirror_skips_creates_for_directories_its_copies_will_make()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\sub\new.txt", "new", t);          // copy creates sub\ implicitly

        var ops = PlanFor(fs, SyncStrategy.Mirror, "sub/new.txt");

        Assert.Single(ops.OfType<CopyOperation>());
        Assert.Empty(ops.OfType<CreateDirectoryOperation>());
    }

    [Fact]
    public void Mirror_keeps_directories_that_still_exist_in_the_source()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\a\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\a\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\a\orphan.txt", "stale", t);

        var ops = PlanFor(fs, SyncStrategy.Mirror, "a/keep.txt");

        Assert.Equal("a/orphan.txt", Assert.Single(ops.OfType<DeleteOperation>()).RelativePath);
        Assert.Empty(ops.OfType<DeleteDirectoryOperation>());
        Assert.Empty(ops.OfType<CreateDirectoryOperation>());
    }

    [Fact]
    public void Mirror_never_plans_the_destination_root_for_removal()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\orphan.txt", "stale", t);         // orphan directly in the root

        var ops = PlanFor(fs, SyncStrategy.Mirror, "keep.txt");

        Assert.Single(ops.OfType<DeleteOperation>());
        Assert.Empty(ops.OfType<DeleteDirectoryOperation>());
    }

    [Fact]
    public void AddOnly_never_touches_directories()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.EnsureDirectory(@"S:\src\empty");                  // would be created by Mirror
        fs.AddFile(@"D:\dst\gone\orphan.txt", "stale", t);    // would be removed by Mirror

        var ops = PlanFor(fs, SyncStrategy.AddOnly, "keep.txt");

        Assert.Empty(ops.OfType<CreateDirectoryOperation>());
        Assert.Empty(ops.OfType<DeleteDirectoryOperation>());
    }

    [Fact]
    public void AddOnly_never_deletes_orphans()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\keep.txt", "keep", t);
        fs.AddFile(@"D:\dst\orphan.txt", "stale", t);

        var ops = PlanFor(fs, SyncStrategy.AddOnly, "keep.txt");

        Assert.Empty(ops.OfType<DeleteOperation>());
    }

    [Fact]
    public void Move_plans_a_move_for_every_filtered_file()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\b.txt", "b");

        var ops = PlanFor(fs, SyncStrategy.Move, "a.txt", "b.txt");

        var moved = ops.OfType<MoveOperation>().Select(o => o.RelativePath).OrderBy(p => p).ToList();
        Assert.Equal(new[] { "a.txt", "b.txt" }, moved);
        Assert.Equal(ops.Count, moved.Count); // nothing but moves
    }

    [Fact]
    public void Copy_carries_the_relative_and_source_paths()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\sub\a.txt", "a");

        var ops = PlanFor(fs, SyncStrategy.AddOnly, "sub/a.txt");

        var copy = Assert.Single(ops.OfType<CopyOperation>());
        Assert.Equal("sub/a.txt", copy.RelativePath);
        Assert.Equal(@"S:\src/sub/a.txt".Replace('\\', '/'), copy.SourceFullPath.Replace('\\', '/'));
        // The destination is addressed by RelativePath through the provider — no absolute dest path.
    }

    [Fact]
    public void Planning_does_not_mutate_the_filesystem()
    {
        var fs = new InMemoryFileSystem();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fs.AddFile(@"S:\src\a.txt", "a", t);
        fs.AddFile(@"D:\dst\orphan.txt", "x", t);
        var before = fs.AllPaths.OrderBy(p => p).ToList();

        _ = PlanFor(fs, SyncStrategy.Mirror, "a.txt");

        Assert.Equal(before, fs.AllPaths.OrderBy(p => p).ToList());
    }

    [Fact]
    public void AddOnly_reads_only_candidate_stamps_and_does_not_walk_the_destination()
    {
        var fs = new InMemoryFileSystem();
        var stamp = FileStamp.Create(4, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile(@"S:\src\same.txt", "same");
        var provider = new ChurningProvider(
            new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase)
            {
                ["same.txt"] = stamp,
            })
        {
            FailEnumeration = true,
        };

        var plan = SyncPlanner.Plan(
            fs, SourceRoot, provider, Dest(SyncStrategy.AddOnly), ["same.txt"], []);

        Assert.Empty(plan.Operations);
        Assert.Equal(0, provider.EnumerationCount);
        Assert.Equal(["same.txt"], provider.StampRequests);
    }

    [Fact]
    public void AddOnly_treats_a_missing_candidate_as_new_without_requesting_a_throwing_stamp()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\new.txt", "new");
        var provider = new ChurningProvider(
            new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase))
        {
            ThrowForMissingStamp = true,
        };

        var plan = SyncPlanner.Plan(
            fs, SourceRoot, provider, Dest(SyncStrategy.AddOnly), ["new.txt"], []);

        Assert.Single(plan.Operations.OfType<CopyOperation>());
    }

    [Fact]
    public void Mirror_snapshot_ignores_files_that_vanish_while_being_stamped()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\keep.txt", "keep");
        var provider = new ChurningProvider(
            new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase)
            {
                ["keep.txt"] = fs.GetStamp(@"S:\src\keep.txt"),
                ["vanished.txt"] = FileStamp.Create(1, DateTime.UtcNow),
            })
        {
            VanishOnStamp = "vanished.txt",
        };

        var plan = SyncPlanner.Plan(
            fs, SourceRoot, provider, Dest(SyncStrategy.Mirror), ["keep.txt"], []);

        Assert.Empty(plan.Operations);
        Assert.Equal(1, plan.DestinationFileCount);
        Assert.Equal(1, provider.EnumerationCount);
    }

    private sealed class ChurningProvider(Dictionary<string, FileStamp> stamps) : IDestinationProvider
    {
        public bool FailEnumeration { get; init; }
        public string? VanishOnStamp { get; init; }
        public bool ThrowForMissingStamp { get; init; }
        public int EnumerationCount { get; private set; }
        public List<string> StampRequests { get; } = [];
        public DestinationCapabilities Capabilities => new(IsRemote: false, SupportsRecycle: false);

        public IEnumerable<string> Enumerate()
        {
            EnumerationCount++;
            if (FailEnumeration)
            {
                throw new IOException("Unrelated destination tree is unavailable.");
            }

            return stamps.Keys.ToList();
        }

        public IEnumerable<string> EnumerateDirectories() => [];

        public FileStamp GetStamp(string relativePath)
        {
            StampRequests.Add(relativePath);
            if (string.Equals(relativePath, VanishOnStamp, StringComparison.OrdinalIgnoreCase))
            {
                stamps.Remove(relativePath);
                throw new FileNotFoundException("Destination file vanished.", relativePath);
            }

            return stamps.TryGetValue(relativePath, out var stamp)
                ? stamp
                : ThrowForMissingStamp
                    ? throw new InvalidOperationException("Missing stamps must use the non-throwing lookup.")
                    : throw new FileNotFoundException("Destination file is missing.", relativePath);
        }

        public bool TryGetStamp(string relativePath, out FileStamp stamp)
        {
            StampRequests.Add(relativePath);
            return stamps.TryGetValue(relativePath, out stamp);
        }

        public void Write(string relativePath, ISourceFile source, bool verifyContents) =>
            throw new NotSupportedException();

        public void Delete(string relativePath, DeleteMode mode) => throw new NotSupportedException();

        public void EnsureDirectory(string relativePath) => throw new NotSupportedException();

        public void DeleteEmptyDirectory(string relativePath) => throw new NotSupportedException();
    }
}
