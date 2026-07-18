using System.Text.Json.Serialization;

namespace SyncMaid.Core.Triggers;

/// <summary>
/// How a sync task is initiated. Closed hierarchy mirroring the design doc's
/// trigger types; the matching runner for each lives in the engine layer. The
/// JSON discriminators let the source-generated serializer persist the concrete
/// type without reflection.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManualTrigger), "manual")]
[JsonDerivedType(typeof(ScheduledTrigger), "scheduled")]
[JsonDerivedType(typeof(WatchTrigger), "watch")]
public abstract record Trigger;

/// <summary>The task runs only when the user asks it to.</summary>
public sealed record ManualTrigger : Trigger;

/// <summary>The task runs on a schedule described by a cron expression.</summary>
public sealed record ScheduledTrigger(string CronExpression) : Trigger;

/// <summary>
/// The task runs whenever the source changes (filesystem watch), after the source has
/// been quiet for <paramref name="SettleSeconds"/>. Change notifications only say
/// <i>something</i> changed, and programs write in bursts (an image, then its thumbnail,
/// then metadata), so every fresh change restarts the wait and one burst syncs as one
/// run. Set it longer than the longest write burst the source sees (SyncBack calls the
/// same knob "wait a number of seconds for no changes before running").
/// </summary>
public sealed record WatchTrigger(int SettleSeconds = WatchTrigger.DefaultSettleSeconds) : Trigger
{
    /// <summary>Default quiet period — long enough for typical multi-file save bursts.</summary>
    public const int DefaultSettleSeconds = 10;

    /// <summary>Bounds for the editor and for clamping hand-edited config.</summary>
    public const int MinSettleSeconds = 1;

    /// <inheritdoc cref="MinSettleSeconds"/>
    public const int MaxSettleSeconds = 600;

    /// <summary>The quiet period as a <see cref="TimeSpan"/>, clamped to the valid range
    /// so hand-edited config cannot produce a zero or absurd window.</summary>
    public TimeSpan SettleWindow =>
        TimeSpan.FromSeconds(Math.Clamp(SettleSeconds, MinSettleSeconds, MaxSettleSeconds));
}
