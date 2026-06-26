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

/// <summary>The task runs whenever the source changes (filesystem watch).</summary>
public sealed record WatchTrigger : Trigger;
