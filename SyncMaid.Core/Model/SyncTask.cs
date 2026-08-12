using System.Text.Json.Serialization;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Model;

/// <summary>
/// A unit of work: one source synced to one or more destinations, started by a
/// trigger. One-directional only (source → destinations). Immutable record.
/// </summary>
public sealed record SyncTask(
    string Name,
    string SourcePath,
    Trigger Trigger,
    IReadOnlyList<Destination> Destinations)
{
    private readonly SyncTaskKind? _kind;

    /// <summary>
    /// Stable identity, generated once and preserved across edits (via <c>with</c>) so
    /// external state keyed by the task survives renames. Persisted with the task.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// What this task does, and so which destinations it accepts (see
    /// <see cref="SyncTaskKind"/>). Config written before the field existed carries no
    /// value; the destinations imply it, since Move was exclusive then — so it is derived
    /// from them and written back on the next save. A task with no destinations yet has
    /// nothing to derive from and is a Sync task until the user says otherwise, which is
    /// why the field exists at all: it gives a fresh task a shape the editor can tailor to.
    /// </summary>
    [JsonIgnore]
    public SyncTaskKind Kind
    {
        get => _kind ?? (Destinations.Any(destination => destination.Strategy == SyncStrategy.Move)
            ? SyncTaskKind.Move
            : SyncTaskKind.Sync);
        init => _kind = value;
    }

    /// <summary>
    /// The persisted form of <see cref="Kind"/>, and the only reason anything here is
    /// nullable: "the field was never written" has to stay distinguishable from "Sync", or
    /// every legacy Move task would load as a Sync task holding an illegal destination. It
    /// is never null on the way out — the getter resolves the kind — so one save is enough
    /// to make a task explicit.
    /// </summary>
    [JsonPropertyName(nameof(Kind))]
    public SyncTaskKind? PersistedKind
    {
        get => Kind;
        init => _kind = value;
    }

    /// <summary>True when <paramref name="strategy"/> is one this task's kind accepts.</summary>
    public bool Accepts(SyncStrategy strategy) =>
        Kind == SyncTaskKind.Move ? strategy == SyncStrategy.Move : strategy != SyncStrategy.Move;
}
