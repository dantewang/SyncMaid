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

}
