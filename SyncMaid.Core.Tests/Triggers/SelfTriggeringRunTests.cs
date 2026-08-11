using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

/// <summary>
/// What a run that mutates its own watched source actually costs today.
///
/// AGENT.md and the archived improvement backlog both state that a task's trigger source is
/// stopped for the duration of its own run and resumed afterwards, with the polling source
/// re-baselining on resume so the run's own changes are absorbed. No production code calls
/// <see cref="ITriggerSource.Stop"/> — the watcher stays live throughout a run, and
/// view-model coalescing plus idempotent planning absorb the feedback instead.
///
/// These tests pin the observable consequence of that, so the choice between implementing
/// the documented suppression and correcting the documents rests on a measured cost rather
/// than on either description.
/// </summary>
public sealed class SelfTriggeringRunTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "syncmaid-selftrigger-" + Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    public SelfTriggeringRunTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(Source, "b.txt"), "b");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A stray handle must not fail a run that already passed.
        }
    }

    // The watcher is live across the run, so the run's own deletions look like source
    // changes and the next poll fires. That follow-up run is the wasted one.
    [Fact]
    public async Task A_move_run_with_a_live_watcher_costs_exactly_one_extra_no_op_run()
    {
        var fileSystem = new PhysicalFileSystem();
        using var trigger = new PollingWatchTriggerSource(
            fileSystem, Source, TimeSpan.FromMilliseconds(50), settleWindow: TimeSpan.Zero);
        var fires = 0;
        trigger.Fired += (_, _) => fires++;

        trigger.Start();
        Assert.False(trigger.PollOnce()); // baseline, no fire
        Assert.Equal(0, fires);

        var destination = new Destination(
            "D", Destination, [new AllFilesFilter()], SyncStrategy.Move);
        var task = new SyncTask("T", Source, new WatchTrigger(), [destination]);
        var engine = new SyncEngine(fileSystem);

        var first = await engine.ExecuteAsync(task);
        Assert.Equal(SyncOutcome.Success, Assert.Single(first).Outcome);
        Assert.Equal(2, Assert.Single(first).FilesCopied);
        Assert.Empty(Directory.EnumerateFiles(Source)); // Move emptied the source

        // The run's own deletions are a change to the watched tree: the trigger fires.
        Assert.True(trigger.PollOnce());
        Assert.Equal(1, fires);

        // That is the wasted run. It is a no-op — planning is idempotent and the source is
        // now empty — but it still costs a full walk of both trees.
        var second = await engine.ExecuteAsync(task);
        Assert.Equal(0, Assert.Single(second).FilesCopied);

        // And it does not cascade: the no-op changed nothing, so the next poll is quiet.
        Assert.False(trigger.PollOnce());
        Assert.Equal(1, fires);
    }

    // The same shape for Mirror, which writes into the destination rather than mutating the
    // source — included to show the feedback is specific to runs that change the source.
    [Fact]
    public async Task A_mirror_run_does_not_retrigger_itself()
    {
        var fileSystem = new PhysicalFileSystem();
        using var trigger = new PollingWatchTriggerSource(
            fileSystem, Source, TimeSpan.FromMilliseconds(50), settleWindow: TimeSpan.Zero);
        var fires = 0;
        trigger.Fired += (_, _) => fires++;

        trigger.Start();
        Assert.False(trigger.PollOnce());

        var destination = new Destination(
            "D", Destination, [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", Source, new WatchTrigger(), [destination]);

        await new SyncEngine(fileSystem).ExecuteAsync(task);

        // Mirror leaves the source untouched, so nothing feeds back.
        Assert.False(trigger.PollOnce());
        Assert.Equal(0, fires);
    }

    // What the documented suppression would buy: stopping the source across the run makes
    // its resume re-baseline, absorbing the run's own deletions so no follow-up fires.
    // This is the behaviour AGENT.md describes; it passes only because the test calls
    // Stop/Start itself, which no production code does.
    [Fact]
    public async Task Stopping_the_trigger_across_the_run_would_absorb_the_extra_run()
    {
        var fileSystem = new PhysicalFileSystem();
        using var trigger = new PollingWatchTriggerSource(
            fileSystem, Source, TimeSpan.FromMilliseconds(50), settleWindow: TimeSpan.Zero);
        var fires = 0;
        trigger.Fired += (_, _) => fires++;

        trigger.Start();
        Assert.False(trigger.PollOnce());

        var destination = new Destination(
            "D", Destination, [new AllFilesFilter()], SyncStrategy.Move);
        var task = new SyncTask("T", Source, new WatchTrigger(), [destination]);

        trigger.Stop();
        await new SyncEngine(fileSystem).ExecuteAsync(task);
        trigger.Start();

        Assert.False(trigger.PollOnce()); // re-baselined: the run's own changes are absorbed
        Assert.Equal(0, fires);
    }
}
