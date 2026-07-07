using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Records the window/lifetime calls the <see cref="TrayController"/> makes.</summary>
public sealed class FakeShellController : IShellController
{
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public int ShutdownCount { get; private set; }

    public void ShowMainWindow() => ShowCount++;
    public void HideMainWindow() => HideCount++;
    public void Shutdown() => ShutdownCount++;
}
