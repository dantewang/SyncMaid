using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>In-memory <see cref="IAutoStartService"/> recording Enable/Disable calls.</summary>
public sealed class FakeAutoStartService : IAutoStartService
{
    public AutoStartState State { get; set; } = AutoStartState.Disabled;
    public int EnableCount { get; private set; }
    public int DisableCount { get; private set; }

    public AutoStartState GetState() => State;

    public void Enable()
    {
        EnableCount++;
        State = AutoStartState.Enabled;
    }

    public void Disable()
    {
        DisableCount++;
        State = AutoStartState.Disabled;
    }
}
