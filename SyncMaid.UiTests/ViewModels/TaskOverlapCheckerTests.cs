using System.Linq;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class TaskOverlapCheckerTests
{
    private static SyncTask Task(string name, string source, params string[] destinations) =>
        new(name, source, new ManualTrigger(),
            destinations
                .Select(path => new Destination(path, path, [new AllFilesFilter()], SyncStrategy.AddOnly))
                .ToList());

    [Theory]
    [InlineData(@"C:\photos")]        // equal
    [InlineData(@"C:\photos\2026")]   // inside the other source
    [InlineData(@"C:\")]              // containing the other source
    public void Sources_conflict_when_equal_or_nested(string candidateSource)
    {
        var others = new[] { Task("Photos", @"C:\photos") };

        Assert.Equal("Photos", TaskOverlapChecker.FindSourceConflict(others, candidateSource));
    }

    [Fact]
    public void Destinations_conflict_when_equal_or_nested()
    {
        var others = new[] { Task("Backup", @"C:\src", @"D:\backup") };

        Assert.Equal("Backup", TaskOverlapChecker.FindDestinationConflict(others, @"D:\backup\sub"));
        Assert.Null(TaskOverlapChecker.FindDestinationConflict(others, @"D:\backup-other"));
    }

    // Chaining — one task's destination feeding another's source — is explicitly allowed.
    [Fact]
    public void Chained_tasks_do_not_conflict()
    {
        var mover = Task("Inbox mover", @"C:\downloads", @"D:\inbox");
        var backer = Task("Inbox backup", @"D:\inbox", @"E:\archive");

        Assert.Null(TaskOverlapChecker.FindTaskConflict([mover], backer));
        Assert.Null(TaskOverlapChecker.FindTaskConflict([backer], mover));
    }

    [Fact]
    public void Task_conflict_reports_the_offending_task_and_disjoint_tasks_pass()
    {
        var photos = Task("Photos", @"C:\photos", @"D:\photos-backup");
        var overlappingSource = Task("Sub", @"C:\photos\2026", @"E:\other");
        var overlappingDestination = Task("Docs", @"C:\docs", @"D:\photos-backup\docs");
        var disjoint = Task("Music", @"C:\music", @"D:\music-backup");

        Assert.Equal("Photos", TaskOverlapChecker.FindTaskConflict([photos], overlappingSource));
        Assert.Equal("Photos", TaskOverlapChecker.FindTaskConflict([photos], overlappingDestination));
        Assert.Null(TaskOverlapChecker.FindTaskConflict([photos], disjoint));
    }

    [Fact]
    public void Unresolvable_paths_conflict_with_nothing()
    {
        var others = new[] { Task("Photos", @"C:\photos") };

        Assert.Null(TaskOverlapChecker.FindSourceConflict(others, @"\\"));
        Assert.Null(TaskOverlapChecker.FindDestinationConflict(others, ""));
    }
}
