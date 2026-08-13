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
    [InlineData("photos/")]
    [InlineData("/photos/")]
    [InlineData(@"\photos\")]
    public void PathFilter_normalizes_natural_folder_patterns(string pattern)
    {
        var rule = new PathFilter(pattern);

        Assert.Equal("photos", rule.Prefix);
        Assert.True(rule.Matches("photos/img.jpg"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData(@"\")]
    [InlineData(@"///\\")]
    public void Empty_or_slash_only_path_filter_matches_nothing(string pattern)
    {
        var rule = new PathFilter(pattern);

        Assert.False(rule.Matches("file.txt"));
        Assert.False(rule.Matches("nested/file.txt"));
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

    [Theory]
    [InlineData("*.jpg")]
    [InlineData(".jpg")]
    [InlineData("jpg")]
    public void ExtensionFilter_normalizes_natural_extension_patterns(string pattern)
    {
        var rule = new ExtensionFilter(pattern);

        Assert.Equal("jpg", rule.Extension);
        Assert.True(rule.Matches("a/b.jpg"));
    }

    // The motivating case: a naming convention, at whatever depth the files happen to sit.
    [Theory]
    [InlineData("ChatGPT Image 1.png", true)]
    [InlineData("saved/ChatGPT-2.png", true)]           // "**/" also matches deeper
    [InlineData("a/b/c/ChatGPT.png", true)]
    [InlineData("chatgpt lower.PNG", true)]             // case-insensitive, like the others
    [InlineData("saved\\ChatGPT-2.png", true)]          // backslashes normalize
    [InlineData("ChatGPT notes.txt", false)]            // wrong type
    [InlineData("my ChatGPT copy.png", false)]          // the name has to start with it
    public void WildcardFilter_matches_a_name_pattern_at_any_depth(string path, bool expected)
    {
        Assert.Equal(expected, new WildcardFilter("**/ChatGPT*.png").Matches(path));
    }

    // "*" is a segment-local wildcard: letting it swallow separators would silently turn
    // every pattern into a recursive one.
    [Theory]
    [InlineData("photos/a.raw", true)]
    [InlineData("photos/2024/a.raw", false)]
    public void WildcardFilter_star_does_not_cross_a_separator(string path, bool expected)
    {
        Assert.Equal(expected, new WildcardFilter("photos/*.raw").Matches(path));
    }

    [Theory]
    [InlineData("photos/a.raw", true)]                  // "**" may match no segments at all
    [InlineData("photos/2024/summer/a.raw", true)]
    [InlineData("photos/2024/summer/a.jpg", false)]
    [InlineData("scans/2024/a.raw", false)]
    public void WildcardFilter_double_star_spans_any_number_of_segments(string path, bool expected)
    {
        Assert.Equal(expected, new WildcardFilter("photos/**/*.raw").Matches(path));
    }

    [Theory]
    [InlineData("photos", true)]                        // a trailing "**" may match nothing
    [InlineData("photos/2024/a.raw", true)]
    [InlineData("scans/a.raw", false)]
    public void WildcardFilter_trailing_double_star_takes_the_whole_subtree(string path, bool expected)
    {
        Assert.Equal(expected, new WildcardFilter("photos/**").Matches(path));
    }

    [Theory]
    [InlineData("a1.png", true)]
    [InlineData("a12.png", false)]                      // "?" is exactly one character
    public void WildcardFilter_question_mark_matches_one_character(string path, bool expected)
    {
        Assert.Equal(expected, new WildcardFilter("a?.png").Matches(path));
    }

    // Selecting everything is AllFilesFilter's job — the same convention PathFilter follows,
    // and the one that keeps an empty input from quietly syncing the whole source.
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void WildcardFilter_without_a_pattern_selects_nothing(string pattern)
    {
        var rule = new WildcardFilter(pattern);

        Assert.False(rule.Matches("a.png"));
        Assert.False(rule.Matches("nested/a.png"));
    }

    // The cached segments are a private field, which the compiler-generated equality would
    // compare by reference; records must stay value-comparable for round-trips to assert.
    [Fact]
    public void WildcardFilter_compares_by_pattern()
    {
        Assert.Equal(new WildcardFilter("**/*.png"), new WildcardFilter("**/*.png"));
        Assert.NotEqual(new WildcardFilter("**/*.png"), new WildcardFilter("**/*.jpg"));
    }
}
