using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Opens the task's destinations as one workspace and returns the edited list, or null if
    /// cancelled. All of them together: a routing task is a rule set, and which rule catches a
    /// given file is a property of the list, not of any rule on its own.
    /// </summary>
    /// <param name="task">The task whose destinations are being edited.</param>
    /// <param name="expand">A destination to open for editing straight away, or null.</param>
    /// <param name="startWithNewRule">True to open with a new, empty destination already being
    /// edited — what the card's add button means.</param>
    /// <param name="crossTaskConflicts">Probe returning the name of another task owning a
    /// destination that overlaps the given path, or null. Destinations of <em>this</em> task
    /// are compared by the workspace itself, which is the only place that knows the pending
    /// edits.</param>
    Task<IReadOnlyList<Destination>?> EditDestinationsAsync(
        SyncTask task,
        Guid? expand,
        bool startWithNewRule,
        Func<string, string?> crossTaskConflicts);

    /// <summary>Shows a modal yes/no confirmation. Returns true only if the user confirms.</summary>
    /// <param name="title">Dialog heading.</param>
    /// <param name="message">Explanatory body text.</param>
    /// <param name="confirmLabel">Label of the confirming button (e.g. "Delete"). Required —
    /// callers pass a localized resource; a constant default couldn't be one.</param>
    /// <param name="isDestructive">When true, the confirm button is styled as destructive.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = true);
}
