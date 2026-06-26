using SyncMaid.Core.Filtering;

namespace SyncMaid.Core.Tests.Filtering;

public class FilterRuleTests
{
    [Theory]
    [InlineData("a.txt")]
    [InlineData("nested/deep/b.jpg")]
    [InlineData("")]
    public void AllFilesFilter_matches_everything(string path)
    {
        Assert.True(new AllFilesFilter().Matches(path));
    }

    [Theory]
    [InlineData("photos/a.jpg", true)]
    [InlineData("photos", true)]               // the folder itself
    [InlineData("photos/sub/a.jpg", true)]
    [InlineData("photosX/a.jpg", false)]       // not a path-segment boundary
    [InlineData("other/a.jpg", false)]
    public void PathFilter_matches_only_under_prefix(string path, bool expected)
    {
        Assert.Equal(expected, new PathFilter("photos").Matches(path));
    }

    [Fact]
    public void PathFilter_is_separator_and_case_insensitive()
    {
        var rule = new PathFilter("Photos/2024");
        Assert.True(rule.Matches(@"photos\2024\a.jpg"));
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("a.JPG", true)]                // case-insensitive
    [InlineData("nested/a.jpeg", false)]       // different extension
    [InlineData("jpg", false)]                 // no dot, not an extension match
    public void ExtensionFilter_matches_by_extension(string path, bool expected)
    {
        Assert.Equal(expected, new ExtensionFilter("jpg").Matches(path));
    }

    [Fact]
    public void ExtensionFilter_accepts_leading_dot()
    {
        Assert.True(new ExtensionFilter(".png").Matches("logo.png"));
    }
}
