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
/// The planner only <i>reads</i>, and only the destination (existence/stamps/listing via
/// the <see cref="IDestinationProvider"/>); the source arrives pre-listed, stamps
/// included, from the engine's single <see cref="IFileSystem.ListTree"/> walk. It never
/// writes, deletes, or moves.
/// </remarks>
public static class SyncPlanner
{
    /// <summary>
    /// Plans the operations needed to reconcile the destination with the filtered source
    /// set, according to the destination's <see cref="SyncStrategy"/>.
    /// </summary>
    /// <param name="sourceRoot">Absolute path of the source root.</param>
    /// <param name="destinationProvider">The destination, read-only, for existence/stamps/listing.</param>
    /// <param name="destination">The destination definition (strategy, delete mode, verify flag).</param>
    /// <param name="filteredFiles">
    /// Source files (relative paths with stamps) that passed the destination's filters.
    /// </param>
    /// <param name="sourceDirectories">
    /// Every directory under the source root (relative paths with modified times). Used
    /// only by Mirror, which replicates the source directory tree — structure and
    /// directory times — exactly; file filters select files, not structure.
    /// </param>
    public static SyncPlan Plan(
        string sourceRoot,
        IDestinationProvider destinationProvider,
        Destination destination,
        IReadOnlyCollection<ListedFile> filteredFiles,
        IReadOnlyCollection<ListedDirectory> sourceDirectories)
    {
        if (destination.Strategy == SyncStrategy.Move)
        {
            return new SyncPlan(
                PlanMove(sourceRoot, destination, filteredFiles),
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
                    sourceRoot,
                    destination,
                    filteredFiles,
                    relativePath => destinationProvider.TryGetStamp(relativePath, out var stamp)
                        ? stamp
                        : null),
                DestinationFileCount: 0);
        }

        var snapshot = DestinationSnapshot.Create(destinationProvider);
        var operations = PlanMirror(
            sourceRoot, destination, filteredFiles, sourceDirectories, snapshot);

        return new SyncPlan(operations, snapshot.FileCount);
    }

    // AddOnly: copy new and changed files; never delete from the destination.
    private static List<SyncOperation> PlanCopies(
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<ListedFile> filteredFiles,
        Func<string, FileStamp?> destinationStamp)
    {
        var operations = new List<SyncOperation>();
        foreach (var file in filteredFiles)
        {
            if (NeedsCopy(file.Stamp, destinationStamp(file.RelativePath)))
            {
                operations.Add(new CopyOperation(file.RelativePath, RelativePaths.Join(sourceRoot, file.RelativePath))
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
        string sourceRoot,
        Destination destination,
        IReadOnlyCollection<ListedFile> filteredFiles,
        IReadOnlyCollection<ListedDirectory> sourceDirectories,
        DestinationSnapshot destinationSnapshot)
    {
        var sourceDirectoryTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in sourceDirectories)
        {
            sourceDirectoryTimes[directory.RelativePath] = directory.LastWriteTimeUtc;
        }

        // Ancestors of the filtered files are source directories by definition; folding
        // them in guards against a directory listing that raced a concurrent change.
        // (They carry no listed time, so they get no timestamp operation this run.)
        var sourceDirectoryNames = new HashSet<string>(
            sourceDirectoryTimes.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var file in filteredFiles)
        {
            sourceDirectoryNames.UnionWith(AncestorDirectories(file.RelativePath));
        }

        var copies = PlanCopies(
            sourceRoot,
            destination,
            filteredFiles,
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
        operations.AddRange(sourceDirectoryNames
            .Where(directory => !destinationSnapshot.Directories.Contains(directory)
                                && !createdByCopies.Contains(directory))
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase) // parents first
            .Select(directory => new CreateDirectoryOperation(directory)));
        operations.AddRange(copies);

        var keep = new HashSet<string>(
            filteredFiles.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (var destRelative in destinationSnapshot.RelativePaths)
        {
            if (!keep.Contains(destRelative))
            {
                operations.Add(new DeleteOperation(destRelative) { Mode = destination.DeleteMode });
            }
        }

        // Destination directories that no longer exist in the source go after the file
        // deletions that empty them. A child is its parent plus "/…", so it sorts after
        // the parent ordinally; descending order deletes children before parents. The
        // enumerations exclude the roots themselves, so the root is never planned.
        operations.AddRange(destinationSnapshot.Directories
            .Where(directory => !sourceDirectoryNames.Contains(directory))
            .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Select(directory => new DeleteDirectoryOperation(directory)));

        // Directory times go last of all: the operations above bump the times of the
        // directories they touch, and NTFS does not bump a parent when a child's own
        // timestamps change, so one trailing pass converges within the run.
        operations.AddRange(PlanDirectoryTimestamps(operations, sourceDirectoryTimes, destinationSnapshot));

        return operations;
    }

    // A destination directory needs its time (re)set when it is about to be created
    // (its time will read "now"), its current time differs from the source's, or one of
    // this plan's operations changes an entry inside it (which bumps it).
    private static List<SetDirectoryTimestampOperation> PlanDirectoryTimestamps(
        IReadOnlyList<SyncOperation> plannedOperations,
        Dictionary<string, DateTime> sourceDirectoryTimes,
        DestinationSnapshot destinationSnapshot)
    {
        var bumped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in plannedOperations)
        {
            if (ParentDirectory(operation.RelativePath) is { } parent)
            {
                bumped.Add(parent);
            }
        }

        var timestamps = new List<SetDirectoryTimestampOperation>();
        foreach (var (directory, time) in sourceDirectoryTimes
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var missing = !destinationSnapshot.Directories.Contains(directory);
            var mismatched = destinationSnapshot.DirectoryTimes.TryGetValue(directory, out var destinationTime)
                             && destinationTime != time;
            if (missing || mismatched || bumped.Contains(directory))
            {
                timestamps.Add(new SetDirectoryTimestampOperation(directory, time));
            }
        }

        return timestamps;
    }

    // "a/b/c" -> "a/b"; a path directly under the root has no parent to bump (the
    // destination root itself never gets operations).
    private static string? ParentDirectory(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        return separator < 0 ? null : relativePath[..separator];
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
        IReadOnlyCollection<ListedFile> filteredFiles)
    {
        var operations = new List<SyncOperation>();
        foreach (var file in filteredFiles)
        {
            operations.Add(new MoveOperation(file.RelativePath, RelativePaths.Join(sourceRoot, file.RelativePath))
            {
                Verify = destination.VerifyContents,
            });
        }

        return operations;
    }

    /// <summary>
    /// A copy is needed when the destination is missing the file or when the source
    /// and destination stamps differ (size or last-write-time). See
    /// <see cref="FileStamp"/> for why stamps, not hashes. Both stamps come from their
    /// side's tree listing — no per-file stat calls during planning.
    /// </summary>
    private static bool NeedsCopy(FileStamp sourceStamp, FileStamp? destinationStamp) =>
        destinationStamp is null || destinationStamp.Value != sourceStamp;

    /// <summary>One stable view of the destination for existence, stamp, delete, and guard decisions.</summary>
    private sealed class DestinationSnapshot
    {
        private DestinationSnapshot(
            IReadOnlyList<string> relativePaths,
            IReadOnlyDictionary<string, FileStamp> stamps,
            IReadOnlySet<string> directories,
            IReadOnlyDictionary<string, DateTime> directoryTimes)
        {
            RelativePaths = relativePaths;
            Stamps = stamps;
            Directories = directories;
            DirectoryTimes = directoryTimes;
        }

        public IReadOnlyList<string> RelativePaths { get; }
        public IReadOnlyDictionary<string, FileStamp> Stamps { get; }
        public IReadOnlySet<string> Directories { get; }
        public IReadOnlyDictionary<string, DateTime> DirectoryTimes { get; }
        public int FileCount => RelativePaths.Count;

        public static DestinationSnapshot Create(IDestinationProvider provider)
        {
            // One walk yields files, stamps, and directories together; there is no
            // per-file stamping phase (and so no churn window between listing and
            // stamping — a file that vanishes mid-walk simply isn't listed).
            var listing = provider.ListTree();

            var relativePaths = new List<string>(listing.Files.Count);
            // Phase-1 path providers use Windows-style, case-insensitive relative keys.
            // A future case-sensitive provider must expose its comparer in the provider
            // contract before this snapshot is reused for that backend.
            var stamps = new Dictionary<string, FileStamp>(listing.Files.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var file in listing.Files)
            {
                stamps[file.RelativePath] = file.Stamp;
                relativePaths.Add(file.RelativePath);
            }

            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directoryTimes = new Dictionary<string, DateTime>(
                listing.Directories.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var directory in listing.Directories)
            {
                directories.Add(directory.RelativePath);
                directoryTimes[directory.RelativePath] = directory.LastWriteTimeUtc;
            }

            return new DestinationSnapshot(relativePaths, stamps, directories, directoryTimes);
        }
    }
}
