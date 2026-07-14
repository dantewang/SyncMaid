using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class ConfirmViewModelTests
{
    [Fact]
    public void Confirm_returns_true()
    {
        var vm = new ConfirmViewModel("Delete?", "Are you sure?", "Delete");
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.True(result);
    }

    [Fact]
    public void Esc_cancel_closes_as_not_confirmed()
    {
        var vm = new ConfirmViewModel("Delete?", "Are you sure?", "Delete");
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.RequestCancel();

        Assert.False(result); // default → keep, don't delete
    }

    [Fact]
    public void Enter_does_nothing_so_a_destructive_confirm_is_not_accidental()
    {
        var vm = new ConfirmViewModel("Delete?", "Are you sure?", "Delete");
        var closed = false;
        vm.CloseRequested += _ => closed = true;

        Assert.False(vm.RequestAccept()); // not handled
        Assert.False(closed);
    }
}
