using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// A Move task routes its source into several destinations: the destinations are an ordered
/// rule list, a file goes to the first one that matches it, and what nothing matches stays
/// put. Ordering is the whole resolution mechanism, so it is pinned from both sides — the
/// same rule set with the destinations swapped must file the contested file elsewhere.
/// </summary>
public class MoveRoutingTests
{
    private static Destination Move(string name, string path, params FilterRule[] filters) =>
        new(name, path, filters, SyncStrategy.Move);

    private static SyncTask Routing(params Destination[] destinations) =>
        new("sort downloads", @"S:\downloads", new ManualTrigger(), destinations);

    [Fact]
    public async Task A_file_two_destinations_match_is_moved_by_the_first_only()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\invoices\march.pdf", "bill");

        var task = Routing(
            Move("books", @"D:\books", new ExtensionFilter("pdf")),
            Move("bills", @"D:\bills", new PathFilter("invoices")));

        var statuses = await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.All(statuses, status => Assert.Equal(SyncOutcome.Success, status.Outcome));
        Assert.True(fs.FileExists(@"D:\books\invoices\march.pdf"));
        Assert.False(fs.FileExists(@"D:\bills\invoices\march.pdf"));
        Assert.False(fs.FileExists(@"S:\downloads\invoices\march.pdf"));

        // The second destination must not merely fail quietly on a file that is already
        // gone — it must never have been given the file at all.
        Assert.Equal(0, statuses[1].FilesCopied);
    }

    [Fact]
    public async Task Reordering_the_destinations_changes_where_a_contested_file_lands()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\invoices\march.pdf", "bill");

        var task = Routing(
            Move("bills", @"D:\bills", new PathFilter("invoices")),
            Move("books", @"D:\books", new ExtensionFilter("pdf")));

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.True(fs.FileExists(@"D:\bills\invoices\march.pdf"));
        Assert.False(fs.FileExists(@"D:\books\invoices\march.pdf"));
    }

    [Fact]
    public async Task Every_matched_file_lands_in_exactly_one_destination_and_the_rest_stay_put()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\book.pdf", "b");
        fs.AddFile(@"S:\downloads\photo.jpg", "p");
        fs.AddFile(@"S:\downloads\setup.exe", "e");

        var task = Routing(
            Move("books", @"D:\books", new ExtensionFilter("pdf")),
            Move("pictures", @"D:\pictures", new ExtensionFilter("jpg")));

        var statuses = await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.All(statuses, status => Assert.Equal(SyncOutcome.Success, status.Outcome));
        Assert.True(fs.FileExists(@"D:\books\book.pdf"));
        Assert.True(fs.FileExists(@"D:\pictures\photo.jpg"));
        Assert.False(fs.FileExists(@"D:\books\photo.jpg"));
        Assert.False(fs.FileExists(@"D:\pictures\book.pdf"));

        // No rule claims it, so it is not routed anywhere and not deleted either.
        Assert.True(fs.FileExists(@"S:\downloads\setup.exe"));
    }

    // A last all-files rule is the catch-all: under first-match-wins it takes exactly what
    // the rules above it left, which is what makes an inbox empty completely.
    [Fact]
    public async Task A_trailing_all_files_destination_takes_what_the_rules_above_it_left()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\book.pdf", "b");
        fs.AddFile(@"S:\downloads\setup.exe", "e");

        var task = Routing(
            Move("books", @"D:\books", new ExtensionFilter("pdf")),
            Move("to sort", @"D:\to-sort", new AllFilesFilter()));

        await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.True(fs.FileExists(@"D:\books\book.pdf"));
        Assert.True(fs.FileExists(@"D:\to-sort\setup.exe"));
        Assert.False(fs.FileExists(@"D:\to-sort\book.pdf"));
    }

    [Fact]
    public async Task An_unavailable_destination_fails_alone_and_its_files_stay_in_the_source()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\book.pdf", "b");
        fs.AddFile(@"S:\downloads\photo.jpg", "p");
        fs.FailWritePathFragment = @"D:\pictures"; // the drive holding it is gone

        var task = Routing(
            Move("books", @"D:\books", new ExtensionFilter("pdf")),
            Move("pictures", @"D:\pictures", new ExtensionFilter("jpg")));

        var statuses = await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, statuses[0].Outcome);
        Assert.Equal(SyncOutcome.Failed, statuses[1].Outcome);
        Assert.True(fs.FileExists(@"D:\books\book.pdf"));

        // Move deletes the source only once the destination verifies, so the file the
        // failed destination wanted is still there for the next run.
        Assert.True(fs.FileExists(@"S:\downloads\photo.jpg"));
    }

    // Routing is computed from the listing before anything is applied, so the same file is
    // never handed to two destinations — the second one would find its source gone.
    [Fact]
    public void Routing_assigns_each_file_once_and_reports_the_ambiguity()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\downloads\invoices\march.pdf", "bill");
        fs.AddFile(@"S:\downloads\setup.exe", "e");
        var listing = fs.ListTree(@"S:\downloads");

        var routing = MoveRouting.Route(
            [
                Move("books", @"D:\books", new ExtensionFilter("pdf")),
                Move("bills", @"D:\bills", new PathFilter("invoices")),
            ],
            listing.Files);

        Assert.Equal("invoices/march.pdf", Assert.Single(routing.For(0)).RelativePath);
        Assert.Empty(routing.For(1));
        Assert.Equal("setup.exe", Assert.Single(routing.Unmatched).RelativePath);
        Assert.Equal([0, 1], Assert.Contains("invoices/march.pdf", routing.Contested));
    }
}
