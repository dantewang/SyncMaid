using System;
using System.Threading.Tasks;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class DestinationNodeViewModelTests
{
    private static DestinationNodeViewModel New()
    {
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        return new DestinationNodeViewModel(dest, DestinationSyncStatus.Never(dest.Id), _ => Task.CompletedTask, _ => { }, _ => Task.CompletedTask);
    }

    [Fact]
    public void Display_status_shows_the_progress_line_while_set()
    {
        var vm = New();
        vm.MarkRunning();

        vm.SetProgress("Copying a.txt (1/3)");

        Assert.Equal("Copying a.txt (1/3)", vm.DisplayStatus);
    }

    [Fact]
    public void Completing_a_run_clears_the_progress_line_and_shows_the_status()
    {
        var vm = New();
        vm.MarkRunning();
        vm.SetProgress("Copying a.txt (1/3)");

        vm.SetStatus(new DestinationSyncStatus(vm.Id, SyncOutcome.Success, DateTimeOffset.UtcNow, 3, null));

        Assert.DoesNotContain("Copying", vm.DisplayStatus);
        Assert.Contains("Synced", vm.DisplayStatus);
    }
}
