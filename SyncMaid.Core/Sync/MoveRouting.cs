using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Which of a Move task's destinations each source file goes to. A Move task's destinations
/// are an ordered rule list and a file may satisfy several of them, but it can only be moved
/// once — so the first destination in task order that includes it wins, and the rest never
/// see it. Files no destination matches stay in the source.
/// </summary>
/// <remarks>
/// Copying strategies need none of this: they leave the source alone, so each destination
/// can filter the whole listing independently. Routing is computed once per run, before
/// planning, and the editor's preview runs the very same pass so what it shows is what the
/// run will do.
/// </remarks>
public sealed class MoveRouting
{
    private readonly IReadOnlyList<IReadOnlyList<ListedFile>> _assignments;

    private MoveRouting(
        IReadOnlyList<IReadOnlyList<ListedFile>> assignments,
        IReadOnlyList<ListedFile> unmatched,
        IReadOnlyDictionary<string, IReadOnlyList<int>> contested)
    {
        _assignments = assignments;
        Unmatched = unmatched;
        Contested = contested;
    }

    /// <summary>Files that no destination matched; they are left in the source.</summary>
    public IReadOnlyList<ListedFile> Unmatched { get; }

    /// <summary>
    /// Files more than one destination matched, keyed by relative path, with the indexes of
    /// every matching destination in task order — the first is the winner. Shown by the
    /// preview so an ambiguity the ordering resolved is visible rather than silent.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> Contested { get; }

    /// <summary>The files assigned to the destination at <paramref name="destinationIndex"/>.</summary>
    public IReadOnlyList<ListedFile> For(int destinationIndex) => _assignments[destinationIndex];

    /// <summary>Assigns each of <paramref name="files"/> to the first destination that includes it.</summary>
    public static MoveRouting Route(
        IReadOnlyList<Destination> destinations,
        IReadOnlyList<ListedFile> files)
    {
        var assignments = new List<ListedFile>[destinations.Count];
        for (var i = 0; i < assignments.Length; i++)
        {
            assignments[i] = [];
        }

        var unmatched = new List<ListedFile>();
        var contested = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            List<int>? matches = null;
            for (var i = 0; i < destinations.Count; i++)
            {
                if (destinations[i].Includes(file.RelativePath))
                {
                    (matches ??= []).Add(i);
                }
            }

            if (matches is null)
            {
                unmatched.Add(file);
                continue;
            }

            assignments[matches[0]].Add(file);
            if (matches.Count > 1)
            {
                contested[file.RelativePath] = matches;
            }
        }

        return new MoveRouting(assignments, unmatched, contested);
    }
}
