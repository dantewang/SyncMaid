using SyncMaid.Core.Filtering;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class FilterDescriberTests
{
    [Theory]
    [MemberData(nameof(RowDescriptions))]
    public void DescribeRow_uses_the_shared_row_wording(FilterRule rule, string expected)
    {
        Assert.Equal(expected, FilterDescriber.DescribeRow(rule));
    }

    public static TheoryData<FilterRule, string> RowDescriptions => new()
    {
        { new AllFilesFilter(), "All files" },
        { new PathFilter("docs"), "Path: docs" },
        { new ExtensionFilter("jpg"), "Extension: jpg" },
        {
            new AnyOfFilter([new PathFilter("docs"), new ExtensionFilter("jpg")]),
            "docs/ or jpg"
        },
    };
}
