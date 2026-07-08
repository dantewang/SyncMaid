using SyncMaid.Core.Persistence;

namespace SyncMaid.UiTests.Fakes;

/// <summary>In-memory <see cref="IConfigLocationService"/> for settings-dialog tests.</summary>
public sealed class FakeConfigLocationService : IConfigLocationService
{
    public ConfigLocationMode CurrentMode { get; set; } = ConfigLocationMode.AppData;

    /// <summary>What <see cref="CanUse"/> returns (default: yes).</summary>
    public bool CanUseResult { get; set; } = true;

    /// <summary>What <see cref="SwitchTo"/> returns (default: success).</summary>
    public bool SwitchResult { get; set; } = true;

    /// <summary>The mode last requested via <see cref="SwitchTo"/>, or null if never called.</summary>
    public ConfigLocationMode? SwitchedTo { get; private set; }

    public string CurrentDirectory => DirectoryFor(CurrentMode);

    public string DirectoryFor(ConfigLocationMode mode) =>
        mode == ConfigLocationMode.Portable ? @"C:\app\Data" : @"C:\Users\me\AppData\Roaming\SyncMaid";

    public bool CanUse(ConfigLocationMode mode) => CanUseResult;

    public bool SwitchTo(ConfigLocationMode mode)
    {
        SwitchedTo = mode;
        if (SwitchResult)
        {
            CurrentMode = mode;
        }

        return SwitchResult;
    }
}
