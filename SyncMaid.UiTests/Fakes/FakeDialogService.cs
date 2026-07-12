using System;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// Stands in for the editor dialogs. Tests set <see cref="OnEditTask"/> /
/// <see cref="OnEditDestination"/> to decide what the "dialog" returns, and
/// <see cref="ConfirmResult"/> for what a confirmation returns.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    public Func<SyncTask?, SyncTask?> OnEditTask { get; set; } = _ => null;
    public Func<Destination?, Destination?> OnEditDestination { get; set; } = _ => null;

    /// <summary>What <see cref="ConfirmAsync"/> returns (default: confirm).</summary>
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Number of times a confirmation was requested.</summary>
    public int ConfirmCount { get; private set; }

    /// <summary>The <c>hasSiblings</c> flag passed to the most recent destination edit.</summary>
    public bool? LastEditHadSiblings { get; private set; }

    /// <summary>The overlap probes passed to the most recent edits, so tests can assert
    /// the wiring (which tasks a probe sees, and that the edited task excludes itself).</summary>
    public Func<string, string?>? LastSourceConflicts { get; private set; }
    public Func<string, string?>? LastDestinationConflicts { get; private set; }

    public Task<SyncTask?> EditTaskAsync(SyncTask? existing, Func<string, string?> sourceConflicts)
    {
        LastSourceConflicts = sourceConflicts;
        return Task.FromResult(OnEditTask(existing));
    }

    public Task<Destination?> EditDestinationAsync(
        Destination? existing, string sourcePath, bool hasSiblings, Func<string, string?> destinationConflicts)
    {
        LastEditHadSiblings = hasSiblings;
        LastDestinationConflicts = destinationConflicts;
        return Task.FromResult(OnEditDestination(existing));
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", bool isDestructive = true)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }
}
