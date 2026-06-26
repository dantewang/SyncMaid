using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Tests.Model;

public class DestinationTests
{
    private static Destination WithFilters(params FilterRule[] filters) =>
        new("dest", @"D:\dest", filters, SyncStrategy.Mirror);

    [Fact]
    public void Includes_is_false_when_no_rules()
    {
        Assert.False(WithFilters().Includes("a.jpg"));
    }

    [Fact]
    public void Includes_is_true_when_any_rule_matches()
    {
        var dest = WithFilters(new ExtensionFilter("jpg"), new PathFilter("docs"));

        Assert.True(dest.Includes("holiday.jpg"));   // matches the extension rule
        Assert.True(dest.Includes("docs/readme.md")); // matches the path rule
        Assert.False(dest.Includes("music/song.mp3")); // matches neither
    }

    [Fact]
    public void AllFilesFilter_includes_everything()
    {
        Assert.True(WithFilters(new AllFilesFilter()).Includes("anything/at/all.bin"));
    }
}
