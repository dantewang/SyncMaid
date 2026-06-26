using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Sync;

public class SyncApplierTests
{
    [Fact]
    public void Move_copies_then_deletes_source()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, new MoveOperation("a.txt", @"S:\src\a.txt", @"D:\dst\a.txt"));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.False(fs.FileExists(@"S:\src\a.txt"));
    }

    [Fact]
    public void Copy_leaves_source_in_place()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");

        SyncApplier.Apply(fs, new CopyOperation("a.txt", @"S:\src\a.txt", @"D:\dst\a.txt"));

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.True(fs.FileExists(@"S:\src\a.txt"));
    }

    [Fact]
    public void Delete_removes_destination_file()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"D:\dst\a.txt", "a");

        SyncApplier.Apply(fs, new DeleteOperation("a.txt", @"D:\dst\a.txt"));

        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }
}
