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
        SyncPlanner.Plan(fs, SourceRoot, new LocalDestinationProvider(fs, DestRoot), Dest(strategy), filtered).Operations;

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
            fs, SourceRoot, provider, Dest(SyncStrategy.AddOnly), ["same.txt"]);

        Assert.Empty(plan.Operations);
        Assert.Equal(0, provider.EnumerationCount);
        Assert.Equal(["same.txt"], provider.StampRequests);
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
            fs, SourceRoot, provider, Dest(SyncStrategy.Mirror), ["keep.txt"]);

        Assert.Empty(plan.Operations);
        Assert.Equal(1, plan.DestinationFileCount);
        Assert.Equal(1, provider.EnumerationCount);
    }

    private sealed class ChurningProvider(Dictionary<string, FileStamp> stamps) : IDestinationProvider
    {
        public bool FailEnumeration { get; init; }
        public string? VanishOnStamp { get; init; }
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
                : throw new FileNotFoundException("Destination file is missing.", relativePath);
        }

        public void Write(string relativePath, ISourceFile source, bool verifyContents) =>
            throw new NotSupportedException();

        public void Delete(string relativePath, DeleteMode mode) => throw new NotSupportedException();
    }
}
