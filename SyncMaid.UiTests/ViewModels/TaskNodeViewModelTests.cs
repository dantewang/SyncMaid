using System.Linq;
using System.Threading.Tasks;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
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
        System.Action? onChanged = null) =>
        new(
            task,
            dialogs ?? new FakeDialogService(),
            engine ?? new FakeSyncEngine(),
            triggers ?? new FakeTriggerSourceFactory(),
            _ => Task.CompletedTask,
            _ => { },
            onChanged ?? (() => { }));

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
}
