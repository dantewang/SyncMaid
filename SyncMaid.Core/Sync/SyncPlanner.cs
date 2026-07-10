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
/// The planner only <i>reads</i>: source stamps via the source <see cref="IFileSystem"/>,
/// and destination existence/stamps/listing via the <see cref="IDestinationProvider"/>. It
/// never writes, deletes, or moves.
/// </remarks>
public static class SyncPlanner
{
    /// <summary>
    /// Plans the operations needed to reconcile the destination with the filtered source
    /// set, according to the destination's <see cref="SyncStrategy"/>.
    /// </summary>
    /// <param name="sourceFileSystem">The source filesystem, used read-only for source stamps.</param>
    /// <param name="sourceRoot">Absolute path of the source root.</param>
    /// <param name="destinationProvider">The destination, read-only, for existence/stamps/listing.</param>
    /// <param name="destination">The destination definition (strategy, delete mode, verify flag).</param>
    /// <param name="filteredRelativePaths">
    /// Source files (relative paths, forward slashes) that passed the destination's filters.
    /// </param>
    public static SyncPlan Plan(
        IFileSystem sourceFileSystem,
        string sourceRoot,
        IDestinationProvider destinationProvider,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        if (destination.Strategy == SyncStrategy.Move)
        {
            return new SyncPlan(
                PlanMove(sourceRoot, destination, filteredRelativePaths),
                DestinationFileCount: 0);
        }

        if (destination.Strategy is not (SyncStrategy.Mirror or SyncStrategy.AddOnly))
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Strategy,
                "Unknown sync strategy.");
        }

        if (destination.Strategy == SyncStrategy.AddOnly)
        {
            return new SyncPlan(
                PlanCopies(
                    sourceFileSystem,
                    sourceRoot,
                    destination,
                    filteredRelativePaths,
                    relativePath => GetStampOrMissing(destinationProvider, relativePath)),
                DestinationFileCount: 0);
        }

        var snapshot = DestinationSnapshot.Create(destinationProvider);
        var operations = PlanMirror(
            sourceFileSystem, sourceRoot, destination, filteredRelativePaths, snapshot);

        return new SyncPlan(operations, snapshot.FileCount);
    }

    // AddOnly: copy new and changed files; never delete from the destination.
    private static List<SyncOperation> PlanCopies(
        IFileSystem sourceFileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths,
        Func<string, FileStamp?> destinationStamp)
    {
        var operations = new List<SyncOperation>();
        foreach (var relativePath in filteredRelativePaths)
        {
            var sourceFull = RelativePaths.Join(sourceRoot, relativePath);

            if (NeedsCopy(sourceFileSystem, sourceFull, destinationStamp(relativePath)))
            {
                operations.Add(new CopyOperation(relativePath, sourceFull)
                {
                    Verify = destination.VerifyContents,
                });
            }
        }

        return operations;
    }

    // Mirror: AddOnly's copies, plus delete every destination file not in the filtered set.
    private static List<SyncOperation> PlanMirror(
        IFileSystem sourceFileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths,
        DestinationSnapshot destinationSnapshot)
    {
        var operations = PlanCopies(
            sourceFileSystem,
            sourceRoot,
            destination,
            filteredRelativePaths,
            relativePath => destinationSnapshot.Stamps.TryGetValue(relativePath, out var stamp)
                ? stamp
                : null);

        var keep = new HashSet<string>(filteredRelativePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var destRelative in destinationSnapshot.RelativePaths)
        {
            if (!keep.Contains(destRelative))
            {
                operations.Add(new DeleteOperation(destRelative) { Mode = destination.DeleteMode });
            }
        }

        return operations;
    }

    // Move: move each filtered source file to the destination (copy then remove source).
    private static List<SyncOperation> PlanMove(
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths)
    {
        var operations = new List<SyncOperation>();
        foreach (var relativePath in filteredRelativePaths)
        {
            operations.Add(new MoveOperation(relativePath, RelativePaths.Join(sourceRoot, relativePath))
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
    private static bool NeedsCopy(
        IFileSystem sourceFileSystem,
        string sourceFull,
        FileStamp? destinationStamp)
    {
        if (destinationStamp is null)
        {
            return true;
        }

        return sourceFileSystem.GetStamp(sourceFull) != destinationStamp.Value;
    }

    private static FileStamp? GetStampOrMissing(IDestinationProvider provider, string relativePath)
    {
        try
        {
            return provider.GetStamp(relativePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>One stable view of the destination for existence, stamp, delete, and guard decisions.</summary>
    private sealed class DestinationSnapshot
    {
        private DestinationSnapshot(
            IReadOnlyList<string> relativePaths,
            IReadOnlyDictionary<string, FileStamp> stamps)
        {
            RelativePaths = relativePaths;
            Stamps = stamps;
        }

        public IReadOnlyList<string> RelativePaths { get; }
        public IReadOnlyDictionary<string, FileStamp> Stamps { get; }
        public int FileCount => RelativePaths.Count;

        public static DestinationSnapshot Create(IDestinationProvider provider)
        {
            var relativePaths = new List<string>();
            var stamps = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            foreach (var relativePath in provider.Enumerate())
            {
                try
                {
                    stamps[relativePath] = provider.GetStamp(relativePath);
                    relativePaths.Add(relativePath);
                }
                catch (Exception exception) when (
                    exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Destination churn: it no longer exists, so exclude it from this snapshot.
                }
            }

            return new DestinationSnapshot(relativePaths, stamps);
        }
    }
}
