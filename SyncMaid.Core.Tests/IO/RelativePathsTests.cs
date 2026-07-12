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

    // The editor evaluates these while a UNC path is being typed, so partial prefixes
    // must compare as unrelated instead of throwing (GetFullPath rejects them).
    [Theory]
    [InlineData(@"\\")]
    [InlineData(@"\\server")]
    public void Unresolvable_paths_relate_to_nothing_instead_of_throwing(string partial)
    {
        Assert.False(RelativePaths.AreEquivalent(partial, @"C:\source"));
        Assert.False(RelativePaths.AreEquivalent(@"C:\source", partial));
        Assert.False(RelativePaths.IsDescendantOf(partial, @"C:\source"));
        Assert.False(RelativePaths.IsDescendantOf(@"C:\source", partial));
    }
}
