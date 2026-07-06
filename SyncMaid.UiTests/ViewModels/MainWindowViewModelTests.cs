using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
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
        RecordingStatusStore? statusStore = null,
        IDialogHost? host = null,
        FakeAutoStartService? autoStart = null) =>
        new(
            dialogs ?? new FakeDialogService(),
            store ?? new RecordingTaskStore(),
            statusStore ?? new RecordingStatusStore(),
            new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory(),
            new FakeUiDispatcher(),
            host ?? new DialogHost(),
            autoStart ?? new FakeAutoStartService(),
            new FakeMirrorDeleteConfirmer(),
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
            new FakeAutoStartService(), new FakeMirrorDeleteConfirmer(), NullLoggerFactory.Instance);

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
}
