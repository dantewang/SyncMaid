using SyncMaid.Core.IO;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.IO;

public class InMemoryFileSystemTests
{
    [Fact]
    public void EnumerateFiles_returns_relative_paths_under_root()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\sub\b.txt", "b");
        fs.AddFile(@"S:\other\c.txt", "c");

        var files = fs.EnumerateFiles(@"S:\src").OrderBy(p => p).ToList();

        Assert.Equal(new[] { "a.txt", "sub/b.txt" }, files);
    }

    [Fact]
    public void CopyFile_preserves_stamp()
    {
        var fs = new InMemoryFileSystem();
        var stamp = FileStamp.Create(3, new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc));
        fs.AddFile(@"S:\src\a.txt", System.Text.Encoding.UTF8.GetBytes("abc"), stamp);

        fs.CopyFile(@"S:\src\a.txt", @"D:\dst\a.txt");

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.Equal(stamp, fs.GetStamp(@"D:\dst\a.txt"));
        Assert.True(fs.FileExists(@"S:\src\a.txt")); // copy leaves the source in place
    }

    [Fact]
    public void MoveFile_removes_source()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "abc");

        fs.MoveFile(@"S:\src\a.txt", @"D:\dst\a.txt");

        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
        Assert.False(fs.FileExists(@"S:\src\a.txt"));
    }
}
