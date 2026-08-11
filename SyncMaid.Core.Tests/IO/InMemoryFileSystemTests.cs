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

    // The fake is only useful as evidence where it behaves like the real filesystem. These
    // pin the directory semantics that Core's Mirror assertions lean on: a directory is a
    // thing in its own right, not something implied by the files inside it.
    [Fact]
    public void A_directory_outlives_the_files_it_held()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"D:\dst\sub\a.txt", "a");

        fs.DeleteFile(@"D:\dst\sub\a.txt");

        // Real disk keeps the now-empty directory; only DeleteEmptyDirectory removes it.
        Assert.Contains("sub", fs.EnumerateDirectories(@"D:\dst"));
    }

    [Fact]
    public void DeleteEmptyDirectory_keeps_a_directory_holding_only_an_empty_subdirectory()
    {
        var fs = new InMemoryFileSystem();
        fs.EnsureDirectory(@"D:\dst\parent\child");

        fs.DeleteEmptyDirectory(@"D:\dst\parent");

        // Directory.Delete(path, recursive: false) throws for a child of any kind, and
        // PhysicalFileSystem swallows that and keeps the directory. Checking only files
        // here would let the planner's children-before-parents ordering regress unnoticed.
        Assert.Contains("parent", fs.EnumerateDirectories(@"D:\dst"));
        Assert.Empty(fs.DeletedDirectories);
    }

    [Fact]
    public void DeleteEmptyDirectory_does_not_record_a_directory_that_never_existed()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"D:\dst\a.txt", "a");

        fs.DeleteEmptyDirectory(@"D:\dst\never-here");

        // Otherwise Assert.Contains(fs.DeletedDirectories, ...) proves nothing.
        Assert.Empty(fs.DeletedDirectories);
    }

    [Fact]
    public void Setting_a_directory_time_does_not_create_the_directory()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"D:\dst\a.txt", "a");

        fs.SetDirectoryLastWriteTimeUtc(@"D:\dst\ghost", new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc));

        // PhysicalFileSystem swallows FileNotFound/DirectoryNotFound here, so a missing
        // directory stays missing — it must not be conjured into a tree comparison.
        Assert.DoesNotContain("ghost", fs.EnumerateDirectories(@"D:\dst"));
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
