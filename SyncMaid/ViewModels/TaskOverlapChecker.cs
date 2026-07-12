using System.Collections.Generic;
using System.Linq;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.ViewModels;

/// <summary>
/// The cross-task half of the task shape conventions (AGENT.md): across tasks, same-kind
/// paths never overlap — a source may not equal or nest with another task's source, nor a
/// destination with another task's destination. Destination-to-source relations are
/// deliberately not checked: chaining (one task's destination feeding another's source) is
/// allowed and converges. Callers pass the <em>other</em> tasks; each probe returns the
/// first conflicting task's name, or null.
/// </summary>
public static class TaskOverlapChecker
{
    /// <summary>The task whose source overlaps <paramref name="sourcePath"/>, if any.</summary>
    public static string? FindSourceConflict(IEnumerable<SyncTask> otherTasks, string? sourcePath) =>
        otherTasks.FirstOrDefault(other => RelativePaths.Overlaps(other.SourcePath, sourcePath))?.Name;

    /// <summary>The task owning a destination that overlaps <paramref name="destinationPath"/>, if any.</summary>
    public static string? FindDestinationConflict(IEnumerable<SyncTask> otherTasks, string? destinationPath) =>
        otherTasks.FirstOrDefault(other => other.Destinations
            .Any(destination => RelativePaths.Overlaps(destination.LocalPath, destinationPath)))?.Name;

    /// <summary>Any same-kind conflict between <paramref name="task"/> and the others —
    /// the authoritative run-start check covering hand-edited config.</summary>
    public static string? FindTaskConflict(IEnumerable<SyncTask> otherTasks, SyncTask task)
    {
        foreach (var other in otherTasks)
        {
            if (RelativePaths.Overlaps(other.SourcePath, task.SourcePath))
            {
                return other.Name;
            }

            var destinationsCollide = task.Destinations.Any(destination =>
                other.Destinations.Any(theirs =>
                    RelativePaths.Overlaps(theirs.LocalPath, destination.LocalPath)));
            if (destinationsCollide)
            {
                return other.Name;
            }
        }

        return null;
    }
}
