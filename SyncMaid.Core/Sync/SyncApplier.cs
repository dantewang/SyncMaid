using SyncMaid.Core.IO;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Executes a planned list of <see cref="SyncOperation"/>s against an
/// <see cref="IFileSystem"/>. This is the only place in the engine that mutates the
/// filesystem; planning (<see cref="SyncPlanner"/>) is kept side-effect-free.
/// </summary>
public static class SyncApplier
{
    /// <summary>
    /// Applies a single operation. Exposed so callers (e.g. the engine's per-operation
    /// progress loop) can drive execution one step at a time.
    /// </summary>
    public static void Apply(IFileSystem fileSystem, SyncOperation operation)
    {
        switch (operation)
        {
            case CopyOperation copy:
                fileSystem.CopyFile(copy.SourceFullPath, copy.DestinationFullPath);
                break;

            case DeleteOperation delete:
                fileSystem.DeleteFile(delete.DestinationFullPath);
                break;

            case MoveOperation move:
                // Copy-then-delete rather than a raw move so the transfer works across
                // volumes and so a mid-operation failure leaves the source intact (the
                // destination is the new home; losing it without the source is worse).
                fileSystem.CopyFile(move.SourceFullPath, move.DestinationFullPath);
                fileSystem.DeleteFile(move.SourceFullPath);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation),
                    operation.GetType().Name,
                    "Unknown sync operation.");
        }
    }

    /// <summary>Applies every operation in order.</summary>
    public static void ApplyAll(IFileSystem fileSystem, IEnumerable<SyncOperation> operations)
    {
        foreach (var operation in operations)
        {
            Apply(fileSystem, operation);
        }
    }
}
