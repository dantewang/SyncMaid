using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Services;

/// <summary>
/// Localizer lookup semantics. These run under the English culture pinned by the test
/// bootstrap and don't switch languages, so plain [Fact] is safe; anything that calls
/// <see cref="Localizer.Apply"/> belongs in an [AvaloniaFact] (see
/// LocalizationHeadlessTests).
/// </summary>
public class LocalizerTests
{
    [Fact]
    public void A_missing_key_falls_back_to_the_key_itself() =>
        Assert.Equal("Nope.Missing", Localizer.Instance["Nope.Missing"]);

    [Fact]
    public void Plural_picks_the_One_form_for_exactly_one_and_Other_otherwise()
    {
        Assert.Equal("1 file", Localizer.Plural("Common.FilesCount", 1));
        Assert.Equal("5 files", Localizer.Plural("Common.FilesCount", 5));
        Assert.Equal("0 files", Localizer.Plural("Common.FilesCount", 0));
    }

    [Fact]
    public void Filter_connectives_keep_their_significant_whitespace()
    {
        // The resx entries carry surrounding spaces (xml:space="preserve") because
        // FilterDescriber concatenates connectives verbatim.
        Assert.Equal(" and ", Strings.Filter_And);
        Assert.Equal(" or ", Strings.Filter_Or);
        Assert.Equal("not ", Strings.Filter_Not);
    }
}
