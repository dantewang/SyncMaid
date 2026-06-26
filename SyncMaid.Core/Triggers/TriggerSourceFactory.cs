namespace SyncMaid.Core.Triggers;

/// <summary>
/// Maps a domain <see cref="Trigger"/> (plain data) to a live
/// <see cref="ITriggerSource"/> (behavior). Keeping this mapping in one place means
/// the rest of the app never switches on trigger type.
/// </summary>
public sealed class TriggerSourceFactory : ITriggerSourceFactory
{
    /// <inheritdoc />
    public ITriggerSource Create(Trigger trigger, string sourcePath) => trigger switch
    {
        ManualTrigger => new ManualTriggerSource(),
        ScheduledTrigger scheduled => new ScheduledTriggerSource(scheduled.CronExpression),
        WatchTrigger => new WatchTriggerSource(sourcePath),
        _ => throw new ArgumentOutOfRangeException(
            nameof(trigger),
            trigger.GetType().Name,
            "Unknown trigger type."),
    };
}
