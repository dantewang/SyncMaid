using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class MainWindowViewModelTests
{
    private static SyncTask Task(string name) =>
        new(name, $@"C:\{name}", new ManualTrigger(), []);

    private static MainWindowViewModel New(
        FakeDialogService? dialogs = null,
        RecordingTaskStore? store = null,
        FakeTriggerSourceFactory? triggers = null,
        IStatusStore? statusStore = null,
        IDialogHost? host = null,
        FakeAutoStartService? autoStart = null,
        ISyncEngine? engine = null) =>
        new(
            dialogs ?? new FakeDialogService(),
            store ?? new RecordingTaskStore(),
            statusStore ?? new RecordingStatusStore(),
            engine ?? new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory(),
            new FakeUiDispatcher(),
            host ?? new DialogHost(),
            autoStart ?? new FakeAutoStartService(),
            new FakeMirrorDeleteConfirmer(),
            new FakeAppSettingsService(),
            new FakeConfigLocationService(),
            new FakeAppRestartService(),
            NullLoggerFactory.Instance);

    [Fact]
    public void Loads_existing_tasks_from_the_store_on_construction()
    {
        var store = new RecordingTaskStore([Task("A"), Task("B")]);

        var vm = New(store: store);

        Assert.Collection(
            vm.Nodes,
            n => Assert.Equal("A", n.Name),
            n => Assert.Equal("B", n.Name));
    }

    [Fact]
    public async Task AddTask_adds_a_node_and_persists()
    {
        var store = new RecordingTaskStore();
        var dialogs = new FakeDialogService { OnEditTask = _ => Task("New") };

        var vm = New(dialogs, store);
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Equal("New", Assert.Single(vm.Nodes).Name);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("New", Assert.Single(store.Saved).Name);
    }

    [Fact]
    public async Task AddTask_cancelled_changes_nothing()
    {
        var store = new RecordingTaskStore();
        var dialogs = new FakeDialogService { OnEditTask = _ => null };

        var vm = New(dialogs, store);
        await vm.AddTaskCommand.ExecuteAsync(null);

        Assert.Empty(vm.Nodes);
        Assert.Equal(0, store.SaveCount);
    }

    // Task shape convention: tasks never share same-kind paths. The editors receive
    // live probes over the task list; an edited task is excluded from its own check.
    [Fact]
    public async Task Overlap_probes_see_other_tasks_and_exclude_the_edited_one()
    {
        var a = new SyncTask("A", @"C:\a", new ManualTrigger(),
            [new Destination("d", @"D:\a-backup", [new AllFilesFilter()], SyncStrategy.AddOnly)]);
        var b = new SyncTask("B", @"C:\b", new ManualTrigger(), []);
        var dialogs = new FakeDialogService(); // every "dialog" is cancelled
        var vm = New(dialogs, new RecordingTaskStore([a, b]));

        await vm.AddTaskCommand.ExecuteAsync(null);        // a new task checks against both
        Assert.Equal("A", dialogs.LastSourceConflicts!(@"C:\a\sub"));
        Assert.Equal("B", dialogs.LastSourceConflicts!(@"C:\b"));
        Assert.Null(dialogs.LastSourceConflicts!(@"C:\c"));

        await vm.Nodes[0].EditCommand.ExecuteAsync(null);  // editing A excludes A itself
        Assert.Null(dialogs.LastSourceConflicts!(@"C:\a"));
        Assert.Equal("B", dialogs.LastSourceConflicts!(@"C:\b"));

        await vm.Nodes[0].AddDestinationCommand.ExecuteAsync(null); // A's own destination is no conflict
        Assert.Null(dialogs.LastDestinationConflicts!(@"D:\a-backup"));

        await vm.Nodes[1].AddDestinationCommand.ExecuteAsync(null); // B checks against A's destination
        Assert.Equal("A", dialogs.LastDestinationConflicts!(@"D:\a-backup\sub"));
    }

    [Fact]
    public void Loading_tasks_starts_a_trigger_for_each()
    {
        var triggers = new FakeTriggerSourceFactory();

        New(store: new RecordingTaskStore([Task("A"), Task("B")]), triggers: triggers);

        Assert.Equal(2, triggers.Created.Count);
        Assert.All(triggers.Created, source => Assert.True(source.Started));
    }

    [Fact]
    public void Deleting_a_task_disposes_its_trigger()
    {
        var triggers = new FakeTriggerSourceFactory();
        var vm = New(store: new RecordingTaskStore([Task("A")]), triggers: triggers);
        var source = triggers.Created.Single();

        vm.Nodes[0].DeleteCommand.Execute(null);

        Assert.Empty(vm.Nodes);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void Loads_saved_status_and_shows_it_on_the_destination()
    {
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]);
        var statusStore = new RecordingStatusStore(new Dictionary<System.Guid, DestinationSyncStatus>
        {
            [dest.Id] = new(dest.Id, SyncOutcome.Success, System.DateTimeOffset.UtcNow, 7, null),
        });

        var vm = New(store: new RecordingTaskStore([task]), statusStore: statusStore);

        var child = vm.Nodes[0].Children[0];
        Assert.Equal(SyncOutcome.Success, child.Outcome);
        Assert.Contains("7 files", child.StatusText);
    }

    [Fact]
    public void Running_a_task_persists_status_to_the_status_store()
    {
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]);
        var statusStore = new RecordingStatusStore();
        var vm = New(store: new RecordingTaskStore([task]), statusStore: statusStore);

        vm.Nodes[0].ExecuteCommand.Execute(null);

        Assert.True(statusStore.SaveCount > 0);
        Assert.True(statusStore.Saved.ContainsKey(dest.Id));
    }

    [Fact]
    public void Toggle_expand_all_collapses_every_node_and_updates_the_label()
    {
        var vm = New(store: new RecordingTaskStore([Task("A"), Task("B")]));
        Assert.True(vm.AllExpanded);

        vm.ToggleExpandAllCommand.Execute(null);

        Assert.False(vm.AllExpanded);
        Assert.All(vm.Nodes, n => Assert.False(n.IsExpanded));
        Assert.Equal("Expand all", vm.ExpandCollapseLabel);
    }

    [Fact]
    public void Run_all_runs_only_runnable_tasks()
    {
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var runnable = new SyncTask("A", @"C:\a", new ManualTrigger(), [dest]);
        var empty = new SyncTask("B", @"C:\b", new ManualTrigger(), []);
        var engine = new FakeSyncEngine();
        var vm = new MainWindowViewModel(
            new FakeDialogService(), new RecordingTaskStore([runnable, empty]), new RecordingStatusStore(),
            engine, new FakeTriggerSourceFactory(), new FakeUiDispatcher(), new DialogHost(),
            new FakeAutoStartService(), new FakeMirrorDeleteConfirmer(), new FakeAppSettingsService(),
            new FakeConfigLocationService(), new FakeAppRestartService(), NullLoggerFactory.Instance);

        vm.RunAllCommand.Execute(null);

        Assert.Single(engine.Executed);   // only the task with a destination ran
    }

    [Fact]
    public void Open_settings_command_shows_the_settings_dialog()
    {
        var host = new DialogHost();
        var vm = New(host: host);

        Assert.True(vm.OpenSettingsCommand.CanExecute(null));
        vm.OpenSettingsCommand.Execute(null); // async; ShowAsync sets the dialog synchronously before awaiting

        Assert.True(host.IsOpen);
        Assert.IsType<SettingsViewModel>(host.CurrentDialog);
    }

    [Fact]
    public void Toggle_sidebar_flips_visibility()
    {
        var vm = New();
        Assert.True(vm.IsSidebarVisible);

        vm.ToggleSidebarCommand.Execute(null);

        Assert.False(vm.IsSidebarVisible);
    }

    [Fact]
    public async Task Editing_a_task_preserves_its_destinations()
    {
        var withDest = Task("A") with
        {
            Destinations = [new Destination("D", @"D:\d", [new SyncMaid.Core.Filtering.AllFilesFilter()], SyncStrategy.Mirror)],
        };
        var store = new RecordingTaskStore([withDest]);
        // The editor only returns task-level fields (no destinations).
        var dialogs = new FakeDialogService { OnEditTask = _ => Task("A-renamed") };

        var vm = New(dialogs, store);
        await vm.Nodes[0].EditCommand.ExecuteAsync(null);

        var node = Assert.Single(vm.Nodes);
        Assert.Equal("A-renamed", node.Name);
        Assert.Single(node.Task.Destinations);   // destinations carried over
    }

    [Fact]
    public async Task Deleting_a_task_confirms_first_and_keeps_it_when_cancelled()
    {
        var store = new RecordingTaskStore([Task("A"), Task("B")]);
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var vm = New(dialogs, store);

        await vm.Nodes[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmCount);
        Assert.Equal(2, vm.Nodes.Count);   // cancelled → both remain
    }

    [Fact]
    public async Task Confirming_a_task_delete_removes_it()
    {
        var store = new RecordingTaskStore([Task("A"), Task("B")]);
        var dialogs = new FakeDialogService { ConfirmResult = true };
        var vm = New(dialogs, store);

        await vm.Nodes[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal("B", Assert.Single(vm.Nodes).Name);
    }

    [Fact]
    public async Task Deleting_a_task_prunes_its_orphaned_statuses()
    {
        var dest = new Destination("D", @"D:\d", [new SyncMaid.Core.Filtering.AllFilesFilter()], SyncStrategy.Mirror);
        var task = Task("A") with { Destinations = [dest] };
        var statusStore = new RecordingStatusStore(
            new Dictionary<Guid, DestinationSyncStatus> { [dest.Id] = DestinationSyncStatus.Never(dest.Id) });
        var vm = New(
            new FakeDialogService { ConfirmResult = true },
            new RecordingTaskStore([task]),
            statusStore: statusStore);

        await vm.Nodes[0].DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(dest.Id, statusStore.Saved.Keys);   // orphan pruned on save
    }

    [Fact]
    public async Task Creating_a_node_waits_for_the_status_lock_and_uses_a_snapshot()
    {
        var destination = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var runningTask = new SyncTask("running", @"C:\running", new ManualTrigger(), [destination]);
        var addedTask = new SyncTask("added", @"C:\added", new ManualTrigger(), [destination]);
        var statusStore = new BlockingStatusStore();
        var dialogs = new FakeDialogService { OnEditTask = _ => addedTask };
        var vm = New(
            dialogs: dialogs,
            store: new RecordingTaskStore([runningTask]),
            statusStore: statusStore,
            engine: new FakeSyncEngine());

        var run = System.Threading.Tasks.Task.Run(
            () => vm.Nodes[0].ExecuteCommand.ExecuteAsync(null),
            TestContext.Current.CancellationToken);
        Assert.True(statusStore.SaveEntered.Wait(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var add = System.Threading.Tasks.Task.Run(
            () => vm.AddTaskCommand.ExecuteAsync(null),
            TestContext.Current.CancellationToken);

        try
        {
            await System.Threading.Tasks.Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Single(vm.Nodes); // node creation itself must still be waiting for the status lock
            Assert.False(add.IsCompleted);
        }
        finally
        {
            statusStore.AllowSave.Set();
            await run;
            await add;
        }

        Assert.Equal(2, vm.Nodes.Count);
        Assert.Equal(SyncOutcome.Success, vm.Nodes[1].Children[0].Outcome);
    }

    [Fact]
    public async Task Deleting_a_running_task_cancels_its_in_flight_run()
    {
        var destination = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [destination]);
        var engine = new FakeSyncEngine { HangUntilCancelled = true };
        var vm = New(
            dialogs: new FakeDialogService { ConfirmResult = true },
            store: new RecordingTaskStore([task]),
            engine: engine);
        var node = vm.Nodes[0];
        node.ExecuteCommand.Execute(null);

        try
        {
            await node.DeleteCommand.ExecuteAsync(null);

            Assert.True(engine.LastCancellationToken.IsCancellationRequested);
            Assert.Empty(vm.Nodes);
        }
        finally
        {
            node.CancelCommand.Execute(null);
            if (node.ExecuteCommand.ExecutionTask is { } run)
            {
                await run;
            }
        }
    }

    [Fact]
    public async Task Editing_a_running_task_waits_before_the_replacement_can_run()
    {
        var destination = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("A", @"C:\a", new ManualTrigger(), [destination]);
        var engine = new FakeSyncEngine { HangUntilCancelled = true };
        var dialogs = new FakeDialogService
        {
            OnEditTask = existing => existing! with { Name = "A edited" },
        };
        var vm = New(dialogs: dialogs, store: new RecordingTaskStore([task]), engine: engine);
        var original = vm.Nodes[0];
        original.ExecuteCommand.Execute(null);
        var originalToken = engine.LastCancellationToken;

        try
        {
            await original.EditCommand.ExecuteAsync(null);
            engine.HangUntilCancelled = false;
            var replacement = Assert.Single(vm.Nodes);
            await replacement.ExecuteCommand.ExecuteAsync(null);

            Assert.Equal(1, engine.MaxConcurrentExecutions);
            Assert.True(originalToken.IsCancellationRequested);
            Assert.Equal(SyncOutcome.Success, replacement.Children[0].Outcome);
        }
        finally
        {
            original.CancelCommand.Execute(null);
            if (original.ExecuteCommand.ExecutionTask is { } run)
            {
                await run;
            }
        }
    }

    private sealed class BlockingStatusStore : IStatusStore
    {
        public ManualResetEventSlim SaveEntered { get; } = new();
        public ManualResetEventSlim AllowSave { get; } = new();

        public IReadOnlyDictionary<Guid, DestinationSyncStatus> Load() =>
            new Dictionary<Guid, DestinationSyncStatus>();

        public void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses)
        {
            SaveEntered.Set();
            Assert.True(AllowSave.Wait(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
    }
}
