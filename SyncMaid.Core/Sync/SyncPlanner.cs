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
    /// <param name="sourceRelativeDirectories">
    /// Every directory under the source root (relative paths, forward slashes). Used only
    /// by Mirror, which replicates the source directory tree exactly — file filters select
    /// files, not structure.
    /// </param>
    public static SyncPlan Plan(
        IFileSystem sourceFileSystem,
        string sourceRoot,
        IDestinationProvider destinationProvider,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths,
        IReadOnlyCollection<string> sourceRelativeDirectories)
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
                    relativePath => destinationProvider.TryGetStamp(relativePath, out var stamp)
                        ? stamp
                        : null),
                DestinationFileCount: 0);
        }

        var snapshot = DestinationSnapshot.Create(destinationProvider);
        var operations = PlanMirror(
            sourceFileSystem, sourceRoot, destination, filteredRelativePaths, sourceRelativeDirectories, snapshot);

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

    // Mirror: AddOnly's copies, plus delete every destination file not in the filtered
    // set — and reconcile the directory tree itself, so the destination replicates the
    // source structure exactly (empty directories included) and a tree compare of the
    // two reports identical.
    private static List<SyncOperation> PlanMirror(
        IFileSystem sourceFileSystem,
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<string> filteredRelativePaths,
        IReadOnlyCollection<string> sourceRelativeDirectories,
        DestinationSnapshot destinationSnapshot)
    {
        // Ancestors of the filtered files are source directories by definition; folding
        // them in guards against a directory listing that raced a concurrent change.
        var sourceDirectories = new HashSet<string>(sourceRelativeDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in filteredRelativePaths)
        {
            sourceDirectories.UnionWith(AncestorDirectories(relativePath));
        }

        var copies = PlanCopies(
            sourceFileSystem,
            sourceRoot,
            destination,
            filteredRelativePaths,
            relativePath => destinationSnapshot.Stamps.TryGetValue(relativePath, out var stamp)
                ? stamp
                : null);

        // Copies create their parent directories as a side effect, so explicit creates
        // are only planned for directories no copy will touch (typically empty ones).
        var createdByCopies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var copy in copies)
        {
            createdByCopies.UnionWith(AncestorDirectories(copy.RelativePath));
        }

        var operations = new List<SyncOperation>();
        operations.AddRange(sourceDirectories
            .Where(directory => !destinationSnapshot.Directories.Contains(directory)
                                && !createdByCopies.Contains(directory))
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase) // parents first
            .Select(directory => new CreateDirectoryOperation(directory)));
        operations.AddRange(copies);

        var keep = new HashSet<string>(filteredRelativePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var destRelative in destinationSnapshot.RelativePaths)
        {
            if (!keep.Contains(destRelative))
            {
                operations.Add(new DeleteOperation(destRelative) { Mode = destination.DeleteMode });
            }
        }

        // Destination directories that no longer exist in the source go last, after the
        // file deletions that empty them. A child is its parent plus "/…", so it sorts
        // after the parent ordinally; descending order deletes children before parents.
        // The enumerations exclude the roots themselves, so the root is never planned.
        operations.AddRange(destinationSnapshot.Directories
            .Where(directory => !sourceDirectories.Contains(directory))
            .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Select(directory => new DeleteDirectoryOperation(directory)));

        return operations;
    }

    // "a/b/c.txt" yields "a" then "a/b" — every directory between the root (exclusive)
    // and the file. Relative paths use forward slashes throughout the engine.
    private static IEnumerable<string> AncestorDirectories(string relativePath)
    {
        for (var i = relativePath.IndexOf('/'); i >= 0; i = relativePath.IndexOf('/', i + 1))
        {
            yield return relativePath[..i];
        }
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

    /// <summary>One stable view of the destination for existence, stamp, delete, and guard decisions.</summary>
    private sealed class DestinationSnapshot
    {
        private DestinationSnapshot(
            IReadOnlyList<string> relativePaths,
            IReadOnlyDictionary<string, FileStamp> stamps,
            IReadOnlySet<string> directories)
        {
            RelativePaths = relativePaths;
            Stamps = stamps;
            Directories = directories;
        }

        public IReadOnlyList<string> RelativePaths { get; }
        public IReadOnlyDictionary<string, FileStamp> Stamps { get; }
        public IReadOnlySet<string> Directories { get; }
        public int FileCount => RelativePaths.Count;

        public static DestinationSnapshot Create(IDestinationProvider provider)
        {
            var relativePaths = new List<string>();
            // Phase-1 path providers use Windows-style, case-insensitive relative keys.
            // A future case-sensitive provider must expose its comparer in the provider
            // contract before this snapshot is reused for that backend.
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

            var directories = new HashSet<string>(
                provider.EnumerateDirectories(), StringComparer.OrdinalIgnoreCase);
            return new DestinationSnapshot(relativePaths, stamps, directories);
        }
    }
}
