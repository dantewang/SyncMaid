namespace SyncMaid.Core.Model;

/// <summary>
/// What a task does, and therefore which destinations it accepts. The kind is the primary
/// shape rule: Move and the copying strategies have contradictory postconditions (Move
/// empties the source the others treat as the truth), so they never share a task.
/// </summary>
public enum SyncTaskKind
{
    /// <summary>Copies the source to its destinations: Mirror and Add-only. Each destination
    /// filters the whole source independently, so a file may go to several of them.</summary>
    Sync,

    /// <summary>Routes the source away into its destinations: Move only. The destinations are
    /// an ordered rule list and each source file goes to the first one that matches it.</summary>
    Move
}
