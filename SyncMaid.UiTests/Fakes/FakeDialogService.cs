using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Services;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// Stands in for the editor dialogs. Tests set <see cref="OnEditTask"/> /
/// <see cref="OnEditDestination"/> to decide what the "dialog" returns, and
/// <see cref="ConfirmResult"/> for what a confirmation returns.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    public Func<SyncTask?, SyncTask?> OnEditTask { get; set; } = _ => null;

    /// <summary>What the destination workspace "returns": the task's edited destination
    /// list, or null for a cancelled workspace (the default).</summary>
    public Func<SyncTask, IReadOnlyList<Destination>?> OnEditDestinations { get; set; } = _ => null;

    /// <summary>What <see cref="ConfirmAsync"/> returns (default: confirm).</summary>
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Number of times a confirmation was requested.</summary>
    public int ConfirmCount { get; private set; }

    /// <summary>The task whose destinations were opened last, and how: which row the
    /// workspace was told to expand, and whether it opened on a new rule.</summary>
    public SyncTask? LastWorkspaceTask { get; private set; }
    public Guid? LastWorkspaceExpanded { get; private set; }
    public bool LastWorkspaceStartedNewRule { get; private set; }

    /// <summary>The overlap probes passed to the most recent edits, so tests can assert
    /// the wiring (which tasks a probe sees, and that the edited task excludes itself).</summary>
    public Func<string, string?>? LastSourceConflicts { get; private set; }
    public Func<string, string?>? LastDestinationConflicts { get; private set; }

    public Task<SyncTask?> EditTaskAsync(SyncTask? existing, Func<string, string?> sourceConflicts)
    {
        LastSourceConflicts = sourceConflicts;
        return Task.FromResult(OnEditTask(existing));
    }

    public Task<IReadOnlyList<Destination>?> EditDestinationsAsync(
        SyncTask task,
        Guid? expand,
        bool startWithNewRule,
        Func<string, string?> crossTaskConflicts)
    {
        LastWorkspaceTask = task;
        LastWorkspaceExpanded = expand;
        LastWorkspaceStartedNewRule = startWithNewRule;
        LastDestinationConflicts = crossTaskConflicts;
        return Task.FromResult(OnEditDestinations(task));
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", bool isDestructive = true)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }
}
