namespace SyncMaid.Services;

/// <summary>
/// The <see cref="IAutoStartService"/> used on platforms without an autostart implementation
/// (currently everything except Windows): reports <see cref="AutoStartState.Disabled"/> and
/// ignores Enable/Disable. Lets the composition root's platform selector always return a
/// working service, so callers never branch on the OS. macOS (LaunchAgent) and Linux (XDG
/// autostart) would slot in as their own <c>[SupportedOSPlatform]</c> services beside the
/// Windows one — see
/// <see href="https://github.com/dantewang/SyncMaid/wiki/Platform-specific-services"/>.
/// </summary>
public sealed class NoOpAutoStartService : IAutoStartService
{
    public AutoStartState GetState() => AutoStartState.Disabled;

    public void Enable()
    {
    }

    public void Disable()
    {
    }
}
