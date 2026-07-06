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
        FakeMirrorDeleteConfirmer? confirmer = null) =>
        new(
            task,
            statuses ?? new Dictionary<Guid, DestinationSyncStatus>(),
            dialogs ?? new FakeDialogService(),
            engine ?? new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory(),
            new FakeUiDispatcher(),
            _ => Task.CompletedTask,
            _ => { },
            onChanged ?? (() => { }),
            onStatuses ?? (_ => { }),
            logger ?? NullLogger.Instance,
            confirmer ?? new FakeMirrorDeleteConfirmer());

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
            new FakeUiDispatcher(), _ => Task.CompletedTask, _ => { }, () => { }, _ => { }, logger,
            new FakeMirrorDeleteConfirmer());

        Assert.NotNull(node); // degrades to manual-only rather than throwing from the ctor
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is not null);
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
