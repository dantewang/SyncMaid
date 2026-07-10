using SyncMaid.Core.IO;

namespace SyncMaid.Core.Tests.IO;

public class RelativePathsTests
{
    [Theory]
    [InlineData(@"C:\root", "folder/file.txt", @"C:\root/folder/file.txt")]
    [InlineData(@"C:\root\", "folder/file.txt", @"C:\root/folder/file.txt")]
    [InlineData("C:/root/", "folder/file.txt", "C:/root/folder/file.txt")]
    public void Join_normalizes_root_separator(string root, string relativePath, string expected)
    {
        Assert.Equal(expected, RelativePaths.Join(root, relativePath));
    }

    [Fact]
    public void Relationship_is_case_and_separator_insensitive()
    {
        Assert.True(RelativePaths.AreEquivalent(@"C:\Source\", @"c:/source"));
        Assert.True(RelativePaths.IsDescendantOf(@"C:/SOURCE/nested", @"c:\source\"));
        Assert.False(RelativePaths.IsDescendantOf(@"C:\source-other", @"C:\source"));
    }
}
