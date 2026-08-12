using System;
using System.Collections.Generic;
using System.Linq;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class TaskWorkspaceViewModelTests
{
    private static Destination Rule(string name, string extension) =>
        new(name, $@"D:\{name}", [new ExtensionFilter(extension)], SyncStrategy.Move);

    private static SyncTask MoveTask(params Destination[] destinations) =>
        new("Sort downloads", @"C:\downloads", new ManualTrigger(), destinations)
        {
            Kind = SyncTaskKind.Move,
        };

    private static TaskWorkspaceViewModel Workspace(
        SyncTask task,
        Func<string, string?>? crossTaskConflicts = null,
        Guid? expand = null,
        bool startWithNewRule = false) =>
        new(task, new FakeFolderPickerService(), crossTaskConflicts, expand, startWithNewRule);

    private static IReadOnlyList<Destination> Save(TaskWorkspaceViewModel workspace)
    {
        IReadOnlyList<Destination>? saved = null;
        workspace.CloseRequested += result => saved = result;
        workspace.SaveCommand.Execute(null);
        return saved!;
    }

    [Fact]
    public void Rules_are_numbered_in_the_order_they_are_matched()
    {
        var workspace = Workspace(MoveTask(Rule("Books", "pdf"), Rule("Pictures", "jpg")));

        Assert.True(workspace.IsRouting);
        Assert.Equal([1, 2], workspace.Rows.Select(row => row.Number));
    }

    // The order is the whole resolution mechanism for overlapping rules, so moving a row is
    // an edit of the task, and the numbers have to follow it.
    [Fact]
    public void Moving_a_rule_changes_the_saved_order_and_the_numbers()
    {
        var workspace = Workspace(MoveTask(Rule("Books", "pdf"), Rule("Pictures", "jpg")));

        workspace.MoveDownCommand.Execute(workspace.Rows[0]);

        Assert.Equal(["Pictures", "Books"], workspace.Rows.Select(row => row.Name));
        Assert.Equal([1, 2], workspace.Rows.Select(row => row.Number));
        Assert.Equal(["Pictures", "Books"], Save(workspace).Select(destination => destination.Name));
    }

    // The catch-all only means anything last: a rule under "everything else" could never
    // match, so it neither moves down nor lets another rule past it.
    [Fact]
    public void The_catch_all_stays_at_the_end()
    {
        var catchAll = new Destination(
            "Everything else", @"D:\to-sort", [new AllFilesFilter()], SyncStrategy.Move);
        var workspace = Workspace(MoveTask(Rule("Books", "pdf"), catchAll));

        workspace.MoveDownCommand.Execute(workspace.Rows[0]);   // would put it below the catch-all
        workspace.MoveUpCommand.Execute(workspace.Rows[1]);     // would lift the catch-all

        Assert.Equal(["Books", "Everything else"], workspace.Rows.Select(row => row.Name));
        Assert.True(workspace.Rows[1].IsCatchAll);
    }

    [Fact]
    public void The_catch_all_is_offered_once_and_only_for_a_move_task()
    {
        var workspace = Workspace(MoveTask(Rule("Books", "pdf")));
        Assert.True(workspace.CanAddCatchAll);

        workspace.AddCatchAllCommand.Execute(null);
        Assert.False(workspace.CanAddCatchAll);

        var syncTask = new SyncTask("Back up", @"C:\src", new ManualTrigger(), []);
        Assert.False(Workspace(syncTask).CanAddCatchAll);
    }

    // A row is only the editor's frame until an edit is applied; backing out of a new rule
    // must not leave a nameless, pathless destination behind.
    [Fact]
    public void A_new_rule_that_is_cancelled_leaves_nothing_behind()
    {
        var workspace = Workspace(MoveTask(Rule("Books", "pdf")), startWithNewRule: true);
        Assert.Equal(2, workspace.Rows.Count);

        workspace.Rows[1].Editor!.CancelCommand.Execute(null);

        Assert.Single(workspace.Rows);
        Assert.Single(Save(workspace));
    }

    [Fact]
    public void Duplicating_a_rule_copies_it_under_a_new_identity()
    {
        var workspace = Workspace(MoveTask(Rule("Books", "pdf")));

        workspace.DuplicateCommand.Execute(workspace.Rows[0]);
        workspace.Rows[1].Editor!.OKCommand.Execute(null);

        Assert.Equal(2, workspace.Rows.Count);
        Assert.NotEqual(workspace.Rows[0].Destination.Id, workspace.Rows[1].Destination.Id);
        Assert.Equal(workspace.Rows[0].Path, workspace.Rows[1].Path);
    }

    // Task shape convention: destinations never overlap. Inside a task that is the
    // workspace's job — it is the only place holding the other rows' pending paths.
    [Fact]
    public void A_rule_overlapping_another_row_is_reported_to_its_editor()
    {
        var workspace = Workspace(
            MoveTask(Rule("Books", "pdf"), Rule("Pictures", "jpg")),
            crossTaskConflicts: path => path.StartsWith(@"E:\other") ? "Other task" : null);

        workspace.Rows[0].ExpandCommand.Execute(null);
        var editor = workspace.Rows[0].Editor!;

        editor.Path = @"D:\Pictures\2026";
        Assert.True(editor.ShowPathHint);
        Assert.False(editor.OKCommand.CanExecute(null));

        editor.Path = @"E:\other";      // now the clash is with a different task
        Assert.False(editor.OKCommand.CanExecute(null));

        editor.Path = @"D:\Books";      // its own row is not a conflict with itself
        Assert.True(editor.OKCommand.CanExecute(null));
    }

    // Overlap is legal under first-match-wins; a rule an earlier one takes *everything* from
    // is the mistake worth naming, and naming is all it does — saving still works.
    [Fact]
    public void A_rule_an_earlier_one_swallows_is_flagged_without_blocking()
    {
        var archives = new Destination(
            "Archives", @"D:\archives", [new ExtensionFilter("gz")], SyncStrategy.Move);
        var tarballs = new Destination(
            "Tarballs", @"D:\tarballs", [new ExtensionFilter("tar.gz")], SyncStrategy.Move);
        var workspace = Workspace(MoveTask(archives, tarballs));

        Assert.False(workspace.Rows[0].IsShadowed);
        Assert.True(workspace.Rows[1].IsShadowed);
        Assert.Contains("Archives", workspace.Rows[1].ShadowedBy);
        Assert.Equal(2, Save(workspace).Count); // advisory only

        workspace.MoveUpCommand.Execute(workspace.Rows[1]);
        Assert.False(workspace.Rows[0].IsShadowed); // ".tar.gz" first is reachable…
        Assert.False(workspace.Rows[1].IsShadowed); // …and ".gz" still catches the rest
    }

    // The routine routing pair overlaps — a PDF under invoices/ matches both — and that is
    // exactly what first-match-wins is for. Flagging it would train the user to ignore the
    // warning.
    [Fact]
    public void Rules_that_merely_overlap_are_not_flagged()
    {
        var workspace = Workspace(MoveTask(
            Rule("Books", "pdf"),
            new Destination("Bills", @"D:\bills", [new PathFilter("invoices")], SyncStrategy.Move)));

        Assert.All(workspace.Rows, row => Assert.False(row.IsShadowed));
    }

    // The preview runs the engine's own assignment, so what it shows is what a run would do
    // — and a contested file is reported as information, naming the rule that won it.
    [Fact]
    public async System.Threading.Tasks.Task The_preview_counts_what_each_rule_takes()
    {
        var fs = new FakeSourceFileSystem().With(
            @"C:\downloads", "book.pdf", "invoices/march.pdf", "setup.exe");

        var workspace = new TaskWorkspaceViewModel(
            MoveTask(
                Rule("Books", "pdf"),
                new Destination("Bills", @"D:\bills", [new PathFilter("invoices")], SyncStrategy.Move)),
            new FakeFolderPickerService(),
            fileSystem: fs);

        Assert.True(workspace.CanPreview);
        await workspace.RescanCommand.ExecuteAsync(null);

        Assert.Contains("3", workspace.PreviewSummary);
        Assert.Contains("2", workspace.Rows[0].PreviewCount);   // both PDFs, first match wins
        Assert.Contains("0", workspace.Rows[1].PreviewCount);
        Assert.Contains("setup.exe", workspace.PreviewUnmatched);

        var contested = Assert.Single(workspace.Contested);
        Assert.Contains("invoices/march.pdf", contested);
        Assert.Contains("Books", contested); // the rule that actually gets it
    }

    // A preview describes one set of rules; once they change it is a claim about something
    // that no longer exists.
    [Fact]
    public async System.Threading.Tasks.Task Changing_the_rules_drops_the_preview()
    {
        var fs = new FakeSourceFileSystem().With(@"C:\downloads", "book.pdf");
        var workspace = new TaskWorkspaceViewModel(
            MoveTask(Rule("Books", "pdf"), Rule("Pictures", "jpg")),
            new FakeFolderPickerService(),
            fileSystem: fs);

        await workspace.RescanCommand.ExecuteAsync(null);
        Assert.True(workspace.HasPreview);

        workspace.MoveDownCommand.Execute(workspace.Rows[0]);

        Assert.False(workspace.HasPreview);
        Assert.All(workspace.Rows, row => Assert.Null(row.PreviewCount));
    }

    // An unreadable source must say so: an empty preview would read as "nothing matches
    // your rules", which is a different problem with a different fix.
    [Fact]
    public async System.Threading.Tasks.Task An_unreadable_source_is_reported_not_shown_as_empty()
    {
        var workspace = new TaskWorkspaceViewModel(
            MoveTask(Rule("Books", "pdf")),
            new FakeFolderPickerService(),
            fileSystem: new FakeSourceFileSystem()); // the source folder does not exist

        await workspace.RescanCommand.ExecuteAsync(null);

        Assert.Contains("not found or unavailable", workspace.PreviewSummary, StringComparison.OrdinalIgnoreCase);
        Assert.All(workspace.Rows, row => Assert.Null(row.PreviewCount));
    }

    [Fact]
    public void A_sync_task_has_no_ordering_semantics_to_show()
    {
        var workspace = Workspace(new SyncTask(
            "Back up", @"C:\src", new ManualTrigger(),
            [new Destination("Mirror", @"D:\mirror", [new AllFilesFilter()], SyncStrategy.Mirror)]));

        Assert.False(workspace.IsRouting);
        Assert.False(workspace.CanAddCatchAll);
        Assert.All(workspace.Rows, row => Assert.False(row.IsShadowed));
    }
}
