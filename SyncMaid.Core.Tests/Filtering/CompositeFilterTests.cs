using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Tests.Filtering;

public class CompositeFilterTests
{
    private static readonly FilterRule Docs = new PathFilter("docs");
    private static readonly FilterRule Jpg = new ExtensionFilter("jpg");
    private static readonly FilterRule Png = new ExtensionFilter("png");

    [Theory]
    [InlineData("docs/a.jpg", true)]     // both match
    [InlineData("docs/a.txt", false)]    // path only
    [InlineData("other/a.jpg", false)]   // extension only
    [InlineData("other/a.txt", false)]   // neither
    public void AllOf_requires_every_rule(string path, bool expected)
    {
        Assert.Equal(expected, new AllOfFilter([Docs, Jpg]).Matches(path));
    }

    [Theory]
    [InlineData("docs/a.txt", true)]     // path only
    [InlineData("other/a.jpg", true)]    // extension only
    [InlineData("docs/a.jpg", true)]     // both
    [InlineData("other/a.txt", false)]   // neither
    public void AnyOf_requires_at_least_one_rule(string path, bool expected)
    {
        Assert.Equal(expected, new AnyOfFilter([Docs, Jpg]).Matches(path));
    }

    [Fact]
    public void Empty_composites_match_nothing()
    {
        // An accidental empty AND must not silently select the whole source.
        Assert.False(new AllOfFilter([]).Matches("a.txt"));
        Assert.False(new AnyOfFilter([]).Matches("a.txt"));
    }

    [Theory]
    [InlineData("a.jpg", false)]
    [InlineData("a.txt", true)]
    public void Not_inverts_the_wrapped_rule(string path, bool expected)
    {
        Assert.Equal(expected, new NotFilter(Jpg).Matches(path));
    }

    [Fact]
    public void Double_negation_cancels_out()
    {
        var rule = new NotFilter(new NotFilter(Jpg));
        Assert.True(rule.Matches("a.jpg"));
        Assert.False(rule.Matches("a.txt"));
    }

    [Theory]
    [InlineData("docs/a.jpg", true)]     // docs and jpg
    [InlineData("photos/b.jpg", true)]   // photos and jpg
    [InlineData("docs/a.txt", false)]    // right folder, wrong type
    [InlineData("other/a.jpg", false)]   // wrong folder
    public void Nested_expression_docs_or_photos_and_jpg(string path, bool expected)
    {
        // (docs OR photos) AND jpg — the guide's canonical example.
        var rule = new AllOfFilter([new AnyOfFilter([Docs, new PathFilter("photos")]), Jpg]);
        Assert.Equal(expected, rule.Matches(path));
    }

    [Theory]
    [InlineData("readme.md", true)]      // matches the loose rule
    [InlineData("docs/a.jpg", true)]     // matches the AND group
    [InlineData("docs/a.txt", false)]
    public void Nested_expression_md_or_docs_and_jpg(string path, bool expected)
    {
        // md OR (docs AND jpg) — the guide's other canonical example.
        var rule = new AnyOfFilter([new ExtensionFilter("md"), new AllOfFilter([Docs, Jpg])]);
        Assert.Equal(expected, rule.Matches(path));
    }

    [Fact]
    public void Not_composes_for_everything_except()
    {
        // docs, but not jpgs — AND with an exclusion.
        var rule = new AllOfFilter([Docs, new NotFilter(new AnyOfFilter([Jpg, Png]))]);

        Assert.True(rule.Matches("docs/report.pdf"));
        Assert.False(rule.Matches("docs/photo.jpg"));
        Assert.False(rule.Matches("docs/photo.png"));
        Assert.False(rule.Matches("other/report.pdf"));
    }

    [Fact]
    public void Composites_compare_by_value()
    {
        // Round-trip assertions rely on record value semantics reaching into the child list.
        Assert.Equal(new AllOfFilter([Docs, Jpg]), new AllOfFilter([Docs, Jpg]));
        Assert.NotEqual(new AllOfFilter([Docs, Jpg]), new AllOfFilter([Jpg, Docs]));
        Assert.Equal(new AnyOfFilter([Docs]), new AnyOfFilter([Docs]));
        Assert.Equal(new NotFilter(Jpg), new NotFilter(Jpg));
    }

    [Fact]
    public void A_composite_works_as_a_destination_filter()
    {
        // Destination.Filters keeps its OR-of-list semantics; a composite is one element of it.
        var destination = new Destination(
            "D", @"D:\d",
            [new AllOfFilter([Docs, Jpg])],
            SyncStrategy.Mirror);

        Assert.True(destination.Includes("docs/a.jpg"));
        Assert.False(destination.Includes("docs/a.txt"));
        Assert.False(destination.Includes("other/a.jpg"));
    }
}
