using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Executes a planned list of <see cref="SyncOperation"/>s: reading from the source
/// <see cref="IFileSystem"/> and writing through the destination's
/// <see cref="IDestinationProvider"/>. This is the only place in the engine that mutates a
/// destination; planning (<see cref="SyncPlanner"/>) is kept side-effect-free.
/// </summary>
public static class SyncApplier
{
    /// <summary>
    /// Applies a single operation. Exposed so callers (e.g. the engine's per-operation
    /// progress loop) can drive execution one step at a time.
    /// </summary>
    public static void Apply(IFileSystem sourceFileSystem, IDestinationProvider destination, SyncOperation operation)
    {
        switch (operation)
        {
            case CopyOperation copy:
                // The provider commits atomically and verifies before replacing the
                // destination (its own strategy — for local, temp → verify → atomic rename).
                destination.Write(copy.RelativePath, SourceFile(sourceFileSystem, copy.RelativePath, copy.SourceFullPath), copy.Verify);
                break;

            case DeleteOperation delete:
                destination.Delete(delete.RelativePath, delete.Mode);
                break;

            case MoveOperation move:
                // Verified move: write (and verify) to the destination, confirm it matches
                // the source, then delete the source — a failed copy never loses the source.
                destination.Write(move.RelativePath, SourceFile(sourceFileSystem, move.RelativePath, move.SourceFullPath), move.Verify);
                if (destination.GetStamp(move.RelativePath) != sourceFileSystem.GetStamp(move.SourceFullPath))
                {
                    throw new SyncVerificationException(
                        $"Refusing to delete source '{move.SourceFullPath}': destination does not match after copy.");
                }

                sourceFileSystem.DeleteFile(move.SourceFullPath);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation),
                    operation.GetType().Name,
                    "Unknown sync operation.");
        }
    }

    /// <summary>Applies every operation in order.</summary>
    public static void ApplyAll(IFileSystem sourceFileSystem, IDestinationProvider destination, IEnumerable<SyncOperation> operations)
    {
        foreach (var operation in operations)
        {
            Apply(sourceFileSystem, destination, operation);
        }
    }

    private static LocalSourceFile SourceFile(IFileSystem sourceFileSystem, string relativePath, string sourceFullPath) =>
        new(sourceFileSystem, relativePath, sourceFullPath);
}
