using SyncMaid.Services;

namespace SyncMaid.UiTests.Services;

public class NoOpAutoStartServiceTests
{
    [Fact]
    public void Reports_disabled()
    {
        Assert.Equal(AutoStartState.Disabled, new NoOpAutoStartService().GetState());
    }

    [Fact]
    public void Enable_and_disable_are_no_ops()
    {
        var service = new NoOpAutoStartService();

        service.Enable();
        service.Disable();

        Assert.Equal(AutoStartState.Disabled, service.GetState()); // unchanged, and no throw
    }
}
