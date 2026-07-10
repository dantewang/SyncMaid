using SyncMaid.Core.Filtering;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class FilterGroupViewModelTests
{
    [Fact]
    public void AddRule_produces_filters_that_match_natural_patterns()
    {
        var group = new FilterGroupViewModel(() => { });

        group.SelectedFilterKind = FilterKind.Path;
        group.NewFilterPattern = "photos/";
        group.AddRuleCommand.Execute(null);

        group.SelectedFilterKind = FilterKind.Extension;
        group.NewFilterPattern = "*.jpg";
        group.AddRuleCommand.Execute(null);

        Assert.Collection(
            group.Rules,
            rule => Assert.True(Assert.IsType<PathFilter>(rule.Rule).Matches("photos/img.png")),
            rule => Assert.True(Assert.IsType<ExtensionFilter>(rule.Rule).Matches("other/img.jpg")));
    }
}
