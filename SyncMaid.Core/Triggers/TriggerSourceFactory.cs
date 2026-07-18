using SyncMaid.Core.IO;

namespace SyncMaid.Core.Triggers;

/// <summary>
/// Maps a domain <see cref="Trigger"/> (plain data) to a live
/// <see cref="ITriggerSource"/> (behavior). Keeping this mapping in one place means
/// the rest of the app never switches on trigger type.
/// </summary>
public sealed class TriggerSourceFactory : ITriggerSourceFactory
{
    private readonly IFileSystem _fileSystem;

    public TriggerSourceFactory(IFileSystem fileSystem) => _fileSystem = fileSystem;

    /// <inheritdoc />
    public ITriggerSource Create(Trigger trigger, string sourcePath) => trigger switch
    {
        ManualTrigger => new ManualTriggerSource(),
        ScheduledTrigger scheduled => new ScheduledTriggerSource(scheduled.CronExpression),
        WatchTrigger watch => CreateWatch(sourcePath, watch.SettleWindow),
        _ => throw new ArgumentOutOfRangeException(
            nameof(trigger),
            trigger.GetType().Name,
            "Unknown trigger type."),
    };

    // FileSystemWatcher is unreliable over UNC / mapped network drives, so a network source
    // is watched by polling instead; local sources use the OS watcher.
    private ITriggerSource CreateWatch(string sourcePath, TimeSpan settleWindow) =>
        NetworkPath.IsNetwork(sourcePath)
            ? new PollingWatchTriggerSource(_fileSystem, sourcePath, settleWindow: settleWindow)
            : new WatchTriggerSource(sourcePath, settleWindow: settleWindow);
}
