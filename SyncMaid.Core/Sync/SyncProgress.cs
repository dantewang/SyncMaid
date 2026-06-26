using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// A single progress update reported while a <see cref="SyncTask"/> runs. Reported
/// just before each operation is applied, so consumers can show "copying X" style
/// status and a completed/total count.
/// </summary>
/// <param name="Destination">The destination currently being synced.</param>
/// <param name="Operation">The operation about to be applied.</param>
/// <param name="CompletedOperations">How many operations have already completed for this destination.</param>
/// <param name="TotalOperations">Total operations planned for this destination.</param>
public readonly record struct SyncProgress(
    Destination Destination,
    SyncOperation Operation,
    int CompletedOperations,
    int TotalOperations);
