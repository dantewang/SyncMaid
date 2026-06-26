using System.Linq;
using System.Threading.Tasks;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class MainWindowViewModelTests
{
    private static SyncTask Task(string name) =>
        new(name, $@"C:\{name}", new ManualTrigger(), []);

    private static MainWindowViewModel New(
        FakeDialogService? dialogs = null,
        RecordingTaskStore? store = null,
        FakeTriggerSourceFactory? triggers = null) =>
        new(
            dialogs ?? new FakeDialogService(),
            store ?? new RecordingTaskStore(),
            new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory());

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
