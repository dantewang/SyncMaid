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

    [Fact]
    public void Move_copies_then_deletes_source()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, dest, new MoveOperation("a.txt", @"S:\src\a.txt"));

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
            SyncApplier.Apply(fs, dest, new MoveOperation("a.txt", @"S:\src\a.txt")));

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
            SyncApplier.Apply(fs, dest, new MoveOperation("a.txt", @"S:\src\a.txt")));

        Assert.Contains("Refusing to delete source", exception.Message);
        Assert.True(fs.FileExists(@"S:\src\a.txt"));
        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public void Copy_leaves_source_in_place()
    {
        var (fs, dest) = Setup();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, dest, new CopyOperation("a.txt", @"S:\src\a.txt"));

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
}
