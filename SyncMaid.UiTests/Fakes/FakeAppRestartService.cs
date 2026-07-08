using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Records restart requests instead of relaunching the process.</summary>
public sealed class FakeAppRestartService : IAppRestartService
{
    public int RestartCount { get; private set; }

    public void Restart() => RestartCount++;
}
