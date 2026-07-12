using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class TaskNodeViewModelTests
{
    private static Destination Dest(string name) =>
        new(name, $@"D:\{name}", [new AllFilesFilter()], SyncStrategy.Mirror);

    private static TaskNodeViewModel New(
        SyncTask task,
        FakeDialogService? dialogs = null,
        FakeSyncEngine? engine = null,
        FakeTriggerSourceFactory? triggers = null,
        Action? onChanged = null,
        Action<IReadOnlyList<DestinationSyncStatus>>? onStatuses = null,
        IReadOnlyDictionary<Guid, DestinationSyncStatus>? statuses = null,
        ILogger? logger = null,
        FakeMirrorDeleteConfirmer? confirmer = null,
        IUiDispatcher? dispatcher = null,
        Func<SyncTask, string?>? runOverlapConflict = null) =>
        new(
            task,
            statuses ?? new Dictionary<Guid, DestinationSyncStatus>(),
            dialogs ?? new FakeDialogService(),
            engine ?? new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory(),
            dispatcher ?? new FakeUiDispatcher(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            onChanged ?? (() => { }),
            onStatuses ?? (_ => { }),
            logger ?? NullLogger.Instance,
            confirmer ?? new FakeMirrorDeleteConfirmer(),
            runOverlapConflict: runOverlapConflict);

    [Fact]
    public void Execute_is_disabled_without_destinations()
    {
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), []));
        Assert.False(node.ExecuteCommand.CanExecute(null));
    }

    [Fact]
    public void Execute_is_enabled_with_destinations_and_runs_the_engine()
    {
        var engine = new FakeSyncEngine();
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]);
        var node = New(task, engine: engine);

        Assert.True(node.ExecuteCommand.CanExecute(null));
        node.ExecuteCommand.Execute(null);

        Assert.Same(task, Assert.Single(engine.Executed));
    }

    [Fact]
    public void A_trigger_start_failure_is_logged_not_swallowed()
    {
        var logger = new RecordingLogger();
        var task = new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]);

        var node = new TaskNodeViewModel(
            task, new Dictionary<Guid, DestinationSyncStatus>(),
            new FakeDialogService(), new FakeSyncEngine(), new ThrowingTriggerSourceFactory(),
            new FakeUiDispatcher(), _ => Task.CompletedTask, _ => Task.CompletedTask, () => { }, _ => { }, logger,
            new FakeMirrorDeleteConfirmer());

        Assert.NotNull(node); // degrades to manual-only rather than throwing from the ctor
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is not null);
    }

    [Fact]
    public void A_scheduled_task_shows_its_next_run()
    {
        var node = New(new SyncTask("A", @"C:\a", new ScheduledTrigger("*/5 * * * *"), [Dest("D")]));

        Assert.True(node.HasNextRun);
        Assert.Contains("next run", node.NextRunText);
        Assert.NotNull(node.NextRunTooltip);
    }

    [Fact]
    public void A_manual_task_has_no_next_run()
    {
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]));

        Assert.False(node.HasNextRun);
        Assert.Equal(string.Empty, node.NextRunText);
    }

    [Fact]
    public void A_trigger_start_failure_surfaces_on_the_card()
    {
        var task = new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]);

        var node = new TaskNodeViewModel(
            task, new Dictionary<Guid, DestinationSyncStatus>(),
            new FakeDialogService(), new FakeSyncEngine(), new ThrowingTriggerSourceFactory(),
            new FakeUiDispatcher(), _ => Task.CompletedTask, _ => Task.CompletedTask, () => { }, _ => { },
            NullLogger.Instance, new FakeMirrorDeleteConfirmer());

        Assert.True(node.HasTriggerError);
        Assert.Contains("bad trigger", node.TriggerError); // carries the underlying reason for the tooltip
    }

    [Fact]
    public void A_healthy_trigger_leaves_no_error_badge()
    {
        var node = New(new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]));

        Assert.False(node.HasTriggerError);
        Assert.Null(node.TriggerError);
    }

    [Fact]
    public void A_runtime_trigger_failure_surfaces_on_the_existing_error_badge()
    {
        var logger = new RecordingLogger();
        var triggers = new FakeTriggerSourceFactory();
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]),
            triggers: triggers,
            logger: logger);

        triggers.Created.Single().RaiseError(new IOException("watcher restart failed"));

        Assert.True(node.HasTriggerError);
        Assert.Contains("watcher restart failed", node.TriggerError);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Exception?.Message == "watcher restart failed");

        triggers.Created.Single().RaiseRecovered();
        Assert.False(node.HasTriggerError);
        Assert.Null(node.TriggerError);
    }

    private sealed class ThrowingTriggerSourceFactory : ITriggerSourceFactory
    {
        public ITriggerSource Create(Trigger trigger, string sourcePath) =>
            throw new InvalidOperationException("bad trigger");
    }

    [Fact]
    public void A_trigger_source_is_created_and_started_for_the_task()
    {
        var triggers = new FakeTriggerSourceFactory();

        New(new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]), triggers: triggers);

        Assert.True(Assert.Single(triggers.Created).Started);
    }

    [Fact]
    public void When_the_trigger_fires_the_sync_runs_automatically()
    {
        // The regression: a watch/scheduled trigger must run the engine without a manual click.
        var engine = new FakeSyncEngine();
        var triggers = new FakeTriggerSourceFactory();
        var task = new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]);
        New(task, engine: engine, triggers: triggers);

        triggers.Created.Single().Raise();

        Assert.Same(task, Assert.Single(engine.Executed));
    }

    [Fact]
    public async Task The_trigger_remains_started_while_a_run_is_active()
    {
        // Run coalescing retains external changes, so trigger sources stay live throughout.
        var engine = new FakeSyncEngine { HangUntilCancelled = true };
        var triggers = new FakeTriggerSourceFactory();
        var node = New(new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]), engine: engine, triggers: triggers);
        var source = triggers.Created.Single();
        Assert.True(source.Started);

        node.ExecuteCommand.Execute(null);
        Assert.True(source.Started);

        node.CancelCommand.Execute(null);
        await node.ExecuteCommand.ExecutionTask!;

        Assert.True(source.Started);
    }

    [Fact]
    public async Task Triggers_during_an_active_run_coalesce_to_exactly_one_follow_up()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeSyncEngine { ExecutionGate = gate.Task };
        var triggers = new FakeTriggerSourceFactory();
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]),
            engine: engine,
            triggers: triggers);
        var source = triggers.Created.Single();

        node.ExecuteCommand.Execute(null);
        source.Raise();
        source.Raise();
        source.Raise();
        gate.SetResult();
        await node.ExecuteCommand.ExecutionTask!;

        Assert.Equal(2, engine.Executed.Count);
        Assert.True(source.Started);
    }

    [Fact]
    public async Task Cancelling_drops_queued_follow_ups_but_allows_a_later_new_run()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeSyncEngine { ExecutionGate = gate.Task };
        var triggers = new FakeTriggerSourceFactory();
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]),
            engine: engine,
            triggers: triggers);

        node.ExecuteCommand.Execute(null);
        triggers.Created.Single().Raise();
        node.CancelCommand.Execute(null);
        gate.TrySetResult();
        await node.ExecuteCommand.ExecutionTask!;

        Assert.Single(engine.Executed);
        await node.ExecuteCommand.ExecuteAsync(null);
        Assert.Equal(2, engine.Executed.Count);
    }

    [Fact]
    public async Task CancelAndWait_rejects_triggers_that_arrive_while_cancellation_drains()
    {
        var cancellationExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeSyncEngine
        {
            HangUntilCancelled = true,
            CancellationExitGate = cancellationExit.Task,
        };
        var triggers = new FakeTriggerSourceFactory();
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [Dest("D")]),
            engine: engine,
            triggers: triggers);
        var source = triggers.Created.Single();
        node.ExecuteCommand.Execute(null);

        var cancellation = node.CancelAndWaitAsync();
        Assert.True(engine.LastCancellationToken.IsCancellationRequested);
        engine.HangUntilCancelled = false;
        source.Raise();
        cancellationExit.SetResult();
        await cancellation;

        Assert.Single(engine.Executed);
        Assert.False(source.Started);
    }

    [Fact]
    public async Task Mass_delete_approval_during_an_active_run_is_preserved_for_the_follow_up()
    {
        var destination = Dest("D");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeSyncEngine
        {
            ExecutionGate = gate.Task,
            NeedsConfirmation = true,
            PreviewResult = new MirrorDeletePreview(19, ["orphan.txt"]),
        };
        var confirmer = new FakeMirrorDeleteConfirmer { Approve = true };
        var triggers = new FakeTriggerSourceFactory();
        var blocked = new DestinationSyncStatus(
            destination.Id, SyncOutcome.NeedsConfirmation, DateTimeOffset.UtcNow, 0, null);
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [destination]),
            engine: engine,
            confirmer: confirmer,
            triggers: triggers,
            statuses: new Dictionary<Guid, DestinationSyncStatus> { [destination.Id] = blocked });

        node.ExecuteCommand.Execute(null);
        await node.Children[0].ConfirmCommand.ExecuteAsync(null);
        triggers.Created.Single().Raise(); // a later plain trigger must not erase the approval
        gate.SetResult();
        await node.ExecuteCommand.ExecutionTask!;

        Assert.Equal(2, engine.Executed.Count);
        Assert.Contains(destination.Id, engine.LastConfirmed!);
        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
    }

    [Fact]
    public async Task Approvals_for_different_destinations_during_one_run_are_all_preserved()
    {
        var first = Dest("A");
        var second = Dest("B");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeSyncEngine
        {
            ExecutionGate = gate.Task,
            NeedsConfirmation = true,
            PreviewResult = new MirrorDeletePreview(19, ["orphan.txt"]),
        };
        var confirmer = new FakeMirrorDeleteConfirmer { Approve = true };
        var triggers = new FakeTriggerSourceFactory();
        var node = New(
            new SyncTask("A", @"C:\a", new WatchTrigger(), [first, second]),
            engine: engine,
            confirmer: confirmer,
            triggers: triggers,
            statuses: new Dictionary<Guid, DestinationSyncStatus>
            {
                [first.Id] = new(first.Id, SyncOutcome.NeedsConfirmation, DateTimeOffset.UtcNow, 0, null),
                [second.Id] = new(second.Id, SyncOutcome.NeedsConfirmation, DateTimeOffset.UtcNow, 0, null),
            });

        node.ExecuteCommand.Execute(null);
        await node.Children[0].ConfirmCommand.ExecuteAsync(null);
        await node.Children[1].ConfirmCommand.ExecuteAsync(null); // must not erase A's approval
        gate.SetResult();
        await node.ExecuteCommand.ExecutionTask!;

        Assert.Equal(2, engine.Executed.Count); // the queued approvals coalesce to one follow-up
        Assert.Contains(first.Id, engine.LastConfirmed!);
        Assert.Contains(second.Id, engine.LastConfirmed!);
        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
        Assert.Equal(SyncOutcome.Success, node.Children[1].Outcome);
    }

    // Task shape convention: tasks never share same-kind paths — a hand-edited overlap
    // is refused at run start, before the engine (and any file) is touched.
    [Fact]
    public async Task A_cross_task_overlap_refuses_the_run_before_the_engine()
    {
        var engine = new FakeSyncEngine();
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]),
            engine: engine,
            runOverlapConflict: _ => "Other");

        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.Empty(engine.Executed);
        Assert.Equal(SyncOutcome.Failed, node.Children[0].Outcome);
        Assert.Contains("\"Other\"", node.Children[0].Status.Error);
    }

    // Task shape convention: Move is exclusive.
    [Fact]
    public void A_move_destination_blocks_adding_another()
    {
        var move = new Destination("m", @"D:\archive", [new AllFilesFilter()], SyncStrategy.Move);
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [move]));

        Assert.False(node.AddDestinationCommand.CanExecute(null));
        Assert.Contains("only destination", node.AddDestinationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sibling_context_flows_to_the_destination_editor()
    {
        var dialogs = new FakeDialogService(); // returns null: the "dialog" is cancelled
        var empty = New(new SyncTask("A", @"C:\a", new ManualTrigger(), []), dialogs: dialogs);
        await empty.AddDestinationCommand.ExecuteAsync(null);
        Assert.False(dialogs.LastEditHadSiblings); // first destination — Move available

        var node = New(new SyncTask("B", @"C:\b", new ManualTrigger(), [Dest("D")]), dialogs: dialogs);
        Assert.True(node.AddDestinationCommand.CanExecute(null)); // non-Move sibling — add allowed
        await node.AddDestinationCommand.ExecuteAsync(null);
        Assert.True(dialogs.LastEditHadSiblings); // …but the new destination cannot be Move
    }

    [Fact]
    public void Disposing_the_node_stops_and_disposes_its_trigger_source()
    {
        var triggers = new FakeTriggerSourceFactory();
        var node = New(new SyncTask("A", @"C:\a", new WatchTrigger(), []), triggers: triggers);
        var source = triggers.Created.Single();

        node.Dispose();
        source.Raise();   // a late event after dispose must not run anything

        Assert.True(source.Disposed);
    }

    [Fact]
    public void Running_then_completing_updates_destination_status_and_reports_it()
    {
        var dest = Dest("D");
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]);
        var engine = new FakeSyncEngine();   // returns Success per destination by default
        IReadOnlyList<DestinationSyncStatus>? reported = null;
        var node = New(task, engine: engine, onStatuses: s => reported = s);

        node.ExecuteCommand.Execute(null);

        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
        Assert.Equal(SyncOutcome.Success, node.HealthOutcome);
        Assert.NotNull(reported);
        Assert.Equal(dest.Id, reported!.Single().DestinationId);
    }

    [Fact]
    public void Loaded_status_is_shown_on_the_destination()
    {
        var dest = Dest("D");
        var status = new DestinationSyncStatus(dest.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0, "boom");
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]),
            statuses: new Dictionary<Guid, DestinationSyncStatus> { [dest.Id] = status });

        Assert.Equal(SyncOutcome.Failed, node.Children[0].Outcome);
        Assert.Contains("boom", node.Children[0].StatusText);
        Assert.Equal("1 of 1 failed", node.HealthText);
    }

    [Fact]
    public async Task Deleting_a_destination_asks_first_and_keeps_it_when_cancelled()
    {
        var persisted = 0;
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]),
            dialogs,
            onChanged: () => persisted++);

        await node.Children[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmCount);
        Assert.Single(node.Children);   // cancelled → still there
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Confirming_removes_the_destination_and_persists()
    {
        var persisted = 0;
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]),
            dialogs,
            onChanged: () => persisted++);

        await node.Children[0].DeleteCommand.ExecuteAsync(null);

        Assert.Empty(node.Children);
        Assert.Equal(1, persisted);
    }

    [Fact]
    public async Task AddDestination_adds_a_child_rebuilds_the_task_and_persists()
    {
        var persisted = 0;
        var dialogs = new FakeDialogService { OnEditDestination = _ => Dest("New") };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), []),
            dialogs,
            onChanged: () => persisted++);

        await node.AddDestinationCommand.ExecuteAsync(null);

        Assert.Equal("New", Assert.Single(node.Children).Name);
        Assert.Equal("New", Assert.Single(node.Task.Destinations).Name);  // task rebuilt
        Assert.Equal(1, persisted);
        Assert.True(node.ExecuteCommand.CanExecute(null));                 // now runnable
    }

    [Fact]
    public async Task AddDestination_cancelled_changes_nothing()
    {
        var persisted = 0;
        var dialogs = new FakeDialogService { OnEditDestination = _ => null };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), []),
            dialogs,
            onChanged: () => persisted++);

        await node.AddDestinationCommand.ExecuteAsync(null);

        Assert.Empty(node.Children);
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Cancelling_a_run_reverts_to_a_neutral_status_not_a_failure()
    {
        var engine = new FakeSyncEngine { HangUntilCancelled = true };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]), engine: engine);

        node.ExecuteCommand.Execute(null);
        Assert.True(node.IsRunning);
        Assert.Equal(SyncOutcome.Running, node.Children[0].Outcome);

        node.CancelCommand.Execute(null);
        await node.ExecuteCommand.ExecutionTask!;

        Assert.False(node.IsRunning);
        Assert.Equal(SyncOutcome.Never, node.Children[0].Outcome); // reverted to before the run, not Failed
    }

    [Fact]
    public async Task Progress_reports_are_shown_on_the_destination_row()
    {
        var dest = Dest("D");
        var engine = new FakeSyncEngine
        {
            HangUntilCancelled = true,
            ProgressToReport = [new SyncProgress(dest, new CopyOperation("photos/img.jpg", @"C:\a\photos\img.jpg"), 2, 10)],
        };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]), engine: engine);

        node.ExecuteCommand.Execute(null); // progress is reported before the run hangs

        Assert.Contains("Copying photos/img.jpg", node.Children[0].DisplayStatus);
        Assert.Contains("(3/10)", node.Children[0].DisplayStatus);

        node.CancelCommand.Execute(null);
        await node.ExecuteCommand.ExecutionTask!;
    }

    [Fact]
    public async Task Unexpected_engine_failure_marks_children_failed_and_releases_the_run_lock()
    {
        var failure = new InvalidOperationException("engine exploded");
        var engine = new FakeSyncEngine { ExceptionToThrow = failure };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]), engine: engine);

        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.False(node.IsRunning);
        Assert.Equal(SyncOutcome.Failed, node.Children[0].Outcome);
        Assert.Contains("engine exploded", node.Children[0].StatusText);

        engine.ExceptionToThrow = null;
        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(2, engine.Executed.Count);
        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
    }

    [Fact]
    public async Task Prologue_failure_releases_the_run_lock_for_a_follow_up_run()
    {
        var engine = new FakeSyncEngine();
        var dispatcher = new FakeUiDispatcher { InvokeException = new IOException("snapshot failed") };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]),
            engine: engine,
            dispatcher: dispatcher);

        await node.ExecuteCommand.ExecuteAsync(null);
        Assert.Empty(engine.Executed);

        dispatcher.InvokeException = null;
        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.Single(engine.Executed);
        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
    }

    [Fact]
    public async Task Children_snapshot_is_taken_through_the_ui_dispatcher()
    {
        var dispatcher = new FakeUiDispatcher();
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]),
            dispatcher: dispatcher);

        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.True(dispatcher.InvokeCount > 0);
    }

    [Fact]
    public void A_blocked_mass_delete_surfaces_as_needs_confirmation()
    {
        var engine = new FakeSyncEngine { NeedsConfirmation = true };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]), engine: engine);

        node.ExecuteCommand.Execute(null);

        Assert.Equal(SyncOutcome.NeedsConfirmation, node.Children[0].Outcome);
        Assert.True(node.Children[0].NeedsConfirmation);
        Assert.Equal(SyncOutcome.NeedsConfirmation, node.HealthOutcome);
    }

    [Fact]
    public async Task An_empty_source_failure_does_not_offer_confirmation()
    {
        var destination = Dest("D");
        var engine = new FakeSyncEngine
        {
            Result =
            [
                new DestinationSyncStatus(
                    destination.Id,
                    SyncOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    Error: "empty-source guard failure"),
            ],
        };
        var confirmer = new FakeMirrorDeleteConfirmer { Approve = true };
        var node = New(
            new SyncTask("A", @"C:\a", new ManualTrigger(), [destination]),
            engine: engine,
            confirmer: confirmer);

        await node.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(SyncOutcome.Failed, node.Children[0].Outcome);
        Assert.False(node.Children[0].NeedsConfirmation);
        Assert.Equal(SyncOutcome.Failed, node.HealthOutcome);
        Assert.Equal(0, confirmer.CallCount);
    }

    [Fact]
    public async Task Confirming_a_mass_delete_reruns_with_the_override()
    {
        var dest = Dest("D");
        var engine = new FakeSyncEngine { NeedsConfirmation = true, PreviewResult = new MirrorDeletePreview(19, ["orphan.txt"]) };
        var confirmer = new FakeMirrorDeleteConfirmer { Approve = true };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]), engine: engine, confirmer: confirmer);

        node.ExecuteCommand.Execute(null);                        // trips the guard
        Assert.Equal(SyncOutcome.NeedsConfirmation, node.Children[0].Outcome);

        await node.Children[0].ConfirmCommand.ExecuteAsync(null); // review → approve → re-run

        Assert.Equal(1, confirmer.CallCount);
        Assert.Contains(dest.Id, engine.LastConfirmed!);          // re-run carried the override
        Assert.Equal(SyncOutcome.Success, node.Children[0].Outcome);
    }

    [Fact]
    public async Task Keeping_the_files_does_not_rerun()
    {
        var engine = new FakeSyncEngine { NeedsConfirmation = true, PreviewResult = new MirrorDeletePreview(19, ["orphan.txt"]) };
        var confirmer = new FakeMirrorDeleteConfirmer { Approve = false };
        var node = New(new SyncTask("A", @"C:\a", new ManualTrigger(), [Dest("D")]), engine: engine, confirmer: confirmer);

        node.ExecuteCommand.Execute(null);
        await node.Children[0].ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmer.CallCount);
        Assert.Single(engine.Executed); // original run only — no re-run
        Assert.Equal(SyncOutcome.NeedsConfirmation, node.Children[0].Outcome);
    }
}
