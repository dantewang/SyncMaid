using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Services;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class ConfirmMirrorDeleteViewModelTests
{
    private static ConfirmMirrorDeleteViewModel New(DeleteMode mode, int count) =>
        new(new MirrorDeleteRequest("Backup", @"F:\backup", mode, new MirrorDeletePreview(count, ["a.txt", "b.txt"])));

    [Fact]
    public void Delete_and_keep_raise_the_decision()
    {
        var vm = New(DeleteMode.Recycle, 5);
        bool? decided = null;
        vm.Decided += result => decided = result;

        vm.KeepCommand.Execute(null);
        Assert.False(decided);

        vm.DeleteCommand.Execute(null);
        Assert.True(decided);
    }

    [Fact]
    public void Explanation_and_button_reflect_recycle_vs_permanent()
    {
        var recycle = New(DeleteMode.Recycle, 5);
        Assert.Contains("Recycle Bin", recycle.Explanation);
        Assert.Contains("Recycle Bin", recycle.ConfirmLabel);

        var permanent = New(DeleteMode.Permanent, 5);
        Assert.Contains("permanently delete", permanent.Explanation);
        Assert.Contains("Delete 5 files", permanent.ConfirmLabel);
    }

    [Fact]
    public void More_text_appears_only_when_the_sample_is_truncated()
    {
        Assert.True(New(DeleteMode.Recycle, 10).HasMore);   // 10 total, 2 sampled
        Assert.False(New(DeleteMode.Recycle, 2).HasMore);   // 2 total, 2 sampled
    }
}
