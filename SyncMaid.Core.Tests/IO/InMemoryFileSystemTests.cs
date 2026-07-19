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
        Assert.Throws<DirectoryNotFoundException>(() => fs.EnumerateFiles(@"S:\missing-directory"));
    }

    [Fact]
    public void ListTree_returns_files_with_stamps_and_directories_from_one_call()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\sub\b.txt", "b");
        fs.EnsureDirectory(@"S:\src\empty");

        var listing = fs.ListTree(@"S:\src");

        Assert.Equal(new[] { "a.txt", "sub/b.txt" }, listing.Files.Select(f => f.RelativePath).OrderBy(p => p));
        Assert.Equal(fs.GetStamp(@"S:\src\a.txt"), listing.Files.Single(f => f.RelativePath == "a.txt").Stamp);
        Assert.Equal(new[] { "empty", "sub" }, listing.Directories.Select(d => d.RelativePath).OrderBy(d => d));
        Assert.Throws<DirectoryNotFoundException>(() => fs.ListTree(@"S:\missing"));
    }

    // Matching PhysicalFileSystem: an unplugged/missing root is not an empty one.
    [Fact]
    public void A_created_but_empty_root_enumerates_empty_while_a_missing_one_throws()
    {
        var fs = new InMemoryFileSystem();
        fs.EnsureDirectory(@"S:\empty");
        fs.AddFile(@"S:\src\a.txt", "a"); // a file implies its root exists

        Assert.Empty(fs.EnumerateFiles(@"S:\empty"));
        Assert.Single(fs.EnumerateFiles(@"S:\src"));
        Assert.Throws<DirectoryNotFoundException>(() => fs.EnumerateFiles(@"S:\missing"));
    }
}
