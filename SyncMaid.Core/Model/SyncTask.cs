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
    IReadOnlyList<Destination> Destinations);
