using System;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.ViewModels;

namespace SyncMaid.Services;

/// <summary>
/// Opens the editor dialogs and returns the edited domain object, or null if cancelled.
/// View models depend on this instead of constructing windows, so they stay free of any
/// view type and remain unit-testable with a fake.
/// </summary>
public interface IDialogService
{
    /// <param name="existing">The task to edit, or null to create a new one.</param>
    /// <param name="sourceConflicts">Probe returning the name of another task whose source
    /// overlaps the given path, or null — sources never overlap across tasks.</param>
    Task<SyncTask?> EditTaskAsync(SyncTask? existing, Func<string, string?> sourceConflicts);

    /// <param name="existing">The destination to edit, or null to create a new one.</param>
    /// <param name="sourcePath">The owning task's source path, used to reject nested paths.</param>
    /// <param name="taskKind">The owning task's kind, which decides the strategy: a Move task's
    /// destinations are routing rules, a Sync task's are Mirror or Add-only.</param>
    /// <param name="destinationConflicts">Probe describing a destination that overlaps the
    /// given path — a sibling of this task or one owned by another task — or null.</param>
    Task<Destination?> EditDestinationAsync(
        Destination? existing,
        string sourcePath,
        SyncTaskKind taskKind,
        Func<string, DestinationConflict?> destinationConflicts);

    /// <summary>Shows a modal yes/no confirmation. Returns true only if the user confirms.</summary>
    /// <param name="title">Dialog heading.</param>
    /// <param name="message">Explanatory body text.</param>
    /// <param name="confirmLabel">Label of the confirming button (e.g. "Delete"). Required —
    /// callers pass a localized resource; a constant default couldn't be one.</param>
    /// <param name="isDestructive">When true, the confirm button is styled as destructive.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = true);
}
