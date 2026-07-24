using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Sync;

public class SyncApplierTests
{
    private const string DestRoot = @"D:\dst";

    private static (InMemoryFileSystem Fs, IDestinationProvider Dest) Setup()
    {
        var fs = new InMemoryFileSystem();
        return (fs, new LocalDestinationProvider(fs, DestRoot));
    }

    // Operations carry the stamp the planner saw; unless a test is exercising the
    // still-being-written check, that is simply the source's current stamp.
    private static CopyOperation Copy(InMemoryFileSystem fs, string relativePath, string sourceFullPath) =>
        new(relativePath, sourceFullPath, fs.GetStamp(sourceFullPath));

    private static MoveOperation Move(InMemoryFileSystem fs, string relativePath, string sourceFullPath) =>
        new(relativePath, sourceFullPath, fs.GetStamp(sourceFullPath));

    [Fact]
    public void Move_copies_then_deletes_source()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, dest, Move(fs, "a.txt", @"S:\src\a.txt"));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.False(fs.FileExists(@"S:\src\a.txt"));
    }

    [Fact]
    public void Move_write_failure_keeps_the_source()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "precious");
        fs.FailWrites = true;

        Assert.ThrowsAny<IOException>(() =>
            SyncApplier.Apply(fs, dest, Move(fs, "a.txt", @"S:\src\a.txt")));

        Assert.True(fs.FileExists(@"S:\src\a.txt"));
        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public void Move_stamp_mismatch_after_copy_keeps_the_source_and_fails()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "precious");
        fs.SetLastWriteTimeOffset = TimeSpan.FromSeconds(1);

        var exception = Assert.Throws<SyncVerificationException>(() =>
            SyncApplier.Apply(fs, dest, Move(fs, "a.txt", @"S:\src\a.txt")));

        Assert.Contains("Refusing to delete source", exception.Message);
        Assert.True(fs.FileExists(@"S:\src\a.txt"));
        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
    }

    // The still-being-written check: the stamp the planner saw is re-verified before the
    // source is read, so a long save in progress is refused rather than half-copied.
    [Fact]
    public void Copy_of_a_source_that_changed_since_planning_is_refused_without_touching_the_destination()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "the first bytes");
        var operation = Copy(fs, "a.txt", @"S:\src\a.txt");
        fs.AddFile(@"S:\src\a.txt", "the first bytes, and more"); // the writer is still going

        Assert.Throws<SourceBusyException>(() => SyncApplier.Apply(fs, dest, operation));

        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public void Move_of_a_source_that_changed_since_planning_is_refused_and_keeps_the_source()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "the first bytes");
        var operation = Move(fs, "a.txt", @"S:\src\a.txt");
        fs.AddFile(@"S:\src\a.txt", "the first bytes, and more");

        Assert.Throws<SourceBusyException>(() => SyncApplier.Apply(fs, dest, operation));

        Assert.True(fs.FileExists(@"S:\src\a.txt"));
        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public void Copy_leaves_source_in_place()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, dest, Copy(fs, "a.txt", @"S:\src\a.txt"));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.True(fs.FileExists(@"S:\src\a.txt"));
    }

    [Fact]
    public void Delete_removes_destination_file()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"D:\dst\a.txt", "a");

        SyncApplier.Apply(fs, dest, new DeleteOperation("a.txt") { Mode = Core.Model.DeleteMode.Permanent });

        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public void Delete_in_recycle_mode_sends_the_file_to_the_recycle_bin()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"D:\dst\a.txt", "a");

        SyncApplier.Apply(fs, dest, new DeleteOperation("a.txt") { Mode = Core.Model.DeleteMode.Recycle });

        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
        Assert.Contains(fs.Recycled, p => p.Contains("a.txt"));
    }

    [Fact]
    public void CreateDirectory_creates_the_destination_directory()
    {
        var (fs, dest) = Setup();
        fs.EnsureDirectory(DestRoot);

        SyncApplier.Apply(fs, dest, new CreateDirectoryOperation("empty/nested"));

        Assert.Contains("empty/nested", fs.EnumerateDirectories(DestRoot));
    }

    [Fact]
    public void SetDirectoryTimestamp_sets_the_destination_directory_time()
    {
        var (fs, dest) = Setup();
        fs.EnsureDirectory(@"D:\dst\a");
        var time = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc);

        SyncApplier.Apply(fs, dest, new SetDirectoryTimestampOperation("a", time));

        Assert.Equal(
            time,
            fs.ListTree(DestRoot).Directories.Single(d => d.RelativePath == "a").LastWriteTimeUtc);
    }

    [Fact]
    public void DeleteDirectory_removes_an_empty_destination_directory()
    {
        var (fs, dest) = Setup();
        fs.EnsureDirectory(@"D:\dst\gone");

        SyncApplier.Apply(fs, dest, new DeleteDirectoryOperation("gone"));

        Assert.Contains(fs.DeletedDirectories, d => d.EndsWith("dst/gone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeleteDirectory_skips_a_directory_that_gained_content_since_planning()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"D:\dst\gone\late.txt", "written after the plan");

        SyncApplier.Apply(fs, dest, new DeleteDirectoryOperation("gone"));

        Assert.True(fs.FileExists(@"D:\dst\gone\late.txt"));
        Assert.Empty(fs.DeletedDirectories);
    }
}
