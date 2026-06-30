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
                // Atomic, verified copy: temp → verify → atomic rename. The existing
                // destination is only ever replaced by a complete, verified file.
                SafeFileTransfer.Copy(fileSystem, copy.SourceFullPath, copy.DestinationFullPath, copy.Verify);
                break;

            case DeleteOperation delete:
                if (delete.Mode == Model.DeleteMode.Recycle)
                {
                    fileSystem.Recycle(delete.DestinationFullPath);
                }
                else
                {
                    fileSystem.DeleteFile(delete.DestinationFullPath);
                }

                break;

            case MoveOperation move:
                // Verified move: atomic copy across, then delete the source only after the
                // destination is confirmed to match — a failed copy never loses the source.
                SafeFileTransfer.Move(fileSystem, move.SourceFullPath, move.DestinationFullPath, move.Verify);
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
