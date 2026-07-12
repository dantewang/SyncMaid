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

    [Theory]
    [InlineData(@"C:\Source\", @"c:/source", true)]              // same location
    [InlineData(@"C:/SOURCE/nested", @"c:\source\", true)]       // nested…
    [InlineData(@"c:\source", @"C:\Source\nested\deep", true)]   // …in either direction
    [InlineData(@"C:\", @"C:\source", true)]                     // drive root contains everything on it
    [InlineData(@"C:\source-other", @"C:\source", false)]        // shared name prefix, not nested
    [InlineData(@"C:\parent\a", @"C:\parent\b", false)]          // siblings under one parent
    public void Overlap_is_case_and_separator_insensitive_and_bidirectional(
        string first, string second, bool expected)
    {
        Assert.Equal(expected, RelativePaths.Overlaps(first, second));
    }

    // The editors evaluate this while a UNC path is being typed, so partial prefixes
    // (which GetFullPath rejects) must overlap nothing instead of throwing.
    [Theory]
    [InlineData(@"\\")]
    [InlineData(@"\\server")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unresolvable_paths_overlap_nothing_instead_of_throwing(string? partial)
    {
        Assert.False(RelativePaths.Overlaps(partial, @"C:\source"));
        Assert.False(RelativePaths.Overlaps(@"C:\source", partial));
    }
}
