using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Turns a desired sync — a filtered source set plus the current destination state —
/// into a list of <see cref="SyncOperation"/>s, <b>without mutating anything</b>.
/// Keeping planning pure makes the engine easy to dry-run and exhaustively test:
/// the same inputs always yield the same plan, and nothing touches disk until
/// <see cref="SyncApplier"/> runs.
/// </summary>
/// <remarks>
/// The planner only <i>reads</i> from the filesystem (file stamps) to decide whether
/// a file changed. It never writes, deletes, or moves.
/// </remarks>
public static class SyncPlanner
{
    /// <summary>
    /// Plans the operations needed to reconcile <paramref name="destination"/> with the
    /// filtered source set, according to the destination's
    /// <see cref="SyncStrategy"/>.
    /// </summary>
    /// <param name="fileSystem">Used read-only, to compare file stamps.</param>
    /// <param name="sourceRoot">Absolute path of the source root.</param>
    /// <param name="destination">The destination being reconciled (path + strategy).</param>
    /// <param name="filteredRelativePaths">
    /// Source files (relative paths, forward slashes) that passed the destination's
    /// filters. These are the only source files considered.
    /// </param>
    public static IReadOnlyList<SyncOperation> Plan(
        IFileSystem fileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        return destination.Strategy switch
        {
            SyncStrategy.Mirror => PlanMirror(fileSystem, sourceRoot, destination, filteredRelativePaths),
            SyncStrategy.AddOnly => PlanCopies(fileSystem, sourceRoot, destination, filteredRelativePaths),
            SyncStrategy.Move => PlanMove(fileSystem, sourceRoot, destination, filteredRelativePaths),
            _ => throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Strategy,
                "Unknown sync strategy."),
        };
    }

    // AddOnly: copy new and changed files; never delete from the destination.
    private static List<SyncOperation> PlanCopies(
        IFileSystem fileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        var operations = new List<SyncOperation>();
        foreach (var relativePath in filteredRelativePaths)
        {
            var sourceFull = Combine(sourceRoot, relativePath);
            var destFull = Combine(destination.Path, relativePath);

            if (NeedsCopy(fileSystem, sourceFull, destFull))
            {
                operations.Add(new CopyOperation(relativePath, sourceFull, destFull)
                {
                    Verify = destination.VerifyContents,
                });
            }
        }

        return operations;
    }

    // Mirror: AddOnly's copies, plus delete every destination file not in the filtered set.
    private static List<SyncOperation> PlanMirror(
        IFileSystem fileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        var operations = PlanCopies(fileSystem, sourceRoot, destination, filteredRelativePaths);

        var keep = new HashSet<string>(filteredRelativePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var destRelative in fileSystem.EnumerateFiles(destination.Path))
        {
            if (!keep.Contains(destRelative))
            {
                operations.Add(new DeleteOperation(destRelative, Combine(destination.Path, destRelative))
                {
                    Mode = destination.DeleteMode,
                });
            }
        }

        return operations;
    }

    // Move: move each filtered source file to the destination (copy then remove source).
    private static List<SyncOperation> PlanMove(
        IFileSystem fileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        var operations = new List<SyncOperation>();
        foreach (var relativePath in filteredRelativePaths)
        {
            var sourceFull = Combine(sourceRoot, relativePath);
            var destFull = Combine(destination.Path, relativePath);
            operations.Add(new MoveOperation(relativePath, sourceFull, destFull)
            {
                Verify = destination.VerifyContents,
            });
        }

        return operations;
    }

    /// <summary>
    /// A copy is needed when the destination is missing the file or when the source
    /// and destination stamps differ (size or last-write-time). See
    /// <see cref="FileStamp"/> for why stamps, not hashes.
    /// </summary>
    private static bool NeedsCopy(IFileSystem fileSystem, string sourceFull, string destFull)
    {
        if (!fileSystem.FileExists(destFull))
        {
            return true;
        }

        return fileSystem.GetStamp(sourceFull) != fileSystem.GetStamp(destFull);
    }

    // Joins a root with a forward-slash relative path. Used to build full paths in the
    // plan; the resulting separators are normalized by the IFileSystem implementation.
    private static string Combine(string root, string relativePath)
    {
        var trimmedRoot = root.TrimEnd('/', '\\');
        return $"{trimmedRoot}/{relativePath}";
    }
}
