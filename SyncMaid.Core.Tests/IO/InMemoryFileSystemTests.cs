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
    public void Missing_files_raise_physical_filesystem_exception_types()
    {
        var fs = new InMemoryFileSystem();

        Assert.Throws<FileNotFoundException>(() => fs.GetStamp(@"S:\missing.txt"));
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllBytes(@"S:\missing.txt"));
        Assert.Throws<FileNotFoundException>(() => fs.OpenRead(@"S:\missing.txt"));
        Assert.Throws<FileNotFoundException>(() => fs.Replace(@"S:\missing.txt", @"D:\target.txt"));
        Assert.Empty(fs.EnumerateFiles(@"S:\missing-directory"));
    }

}
