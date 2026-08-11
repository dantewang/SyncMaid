using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
using SyncMaid.UiTests.Fakes;
using SyncMaid.ViewModels;
using Xunit;

namespace SyncMaid.UiTests.ViewModels;

/// <summary>
/// Sync-core-design §8: <i>"Concurrent runs of one task are serialized (no interleaved
/// writes)."</i> The serialization lives in <see cref="TaskNodeViewModel"/>'s run gate, and
/// the tests around it use <see cref="FakeSyncEngine"/> — which can show that runs did not
/// overlap, but never that the destination came out intact. These drive the <b>real</b>
/// engine over a real temp directory, so the property is asserted where the design states
/// it: on the files.
/// </summary>
public sealed class RunSerializationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "syncmaid-serialize-" + Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    public RunSerializationTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Destination);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A stray handle on a temp tree must not fail the run that just passed.
        }
    }

    [Fact]
    public async Task A_burst_of_triggers_during_a_run_leaves_the_destination_intact()
    {
        // Enough files that an interleaved second run would have room to collide with the
        // first mid-copy rather than slipping between whole operations.
        for (var i = 0; i < 40; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(Source, $"file{i:00}.txt"),
                $"contents {i}",
                TestContext.Current.CancellationToken);
        }

        Directory.CreateDirectory(Path.Combine(Source, "nested"));
        await File.WriteAllTextAsync(
            Path.Combine(Source, "nested", "deep.txt"), "nested", TestContext.Current.CancellationToken);

        var destination = new Destination(
            "D", Destination, [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", Source, new WatchTrigger(), [destination]);
        var triggers = new FakeTriggerSourceFactory();
        var node = NewNode(task, triggers);
        var source = triggers.Created.Single();

        // Fire repeatedly while the first run is in flight; every one of these coalesces
        // into at most one follow-up rather than starting a concurrent writer.
        node.ExecuteCommand.Execute(null);
        for (var i = 0; i < 20; i++)
        {
            source.Raise();
        }

        await node.ExecuteCommand.ExecutionTask!;
        await DrainAsync(node);

        AssertTreesMatch();
        Assert.Empty(
            Directory.EnumerateFiles(Destination, "*.syncmaid-tmp-*", SearchOption.AllDirectories));
    }

    // Watcher callbacks arrive on thread-pool threads, so the run gate is entered
    // concurrently, not just re-entered. (Pressing Run repeatedly would not test this:
    // AsyncRelayCommand already refuses to re-enter, so those calls never reach the gate.)
    [Fact]
    public async Task Triggers_raised_from_several_threads_do_not_interleave_their_writes()
    {
        for (var i = 0; i < 40; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(Source, $"file{i:00}.txt"),
                $"contents {i}",
                TestContext.Current.CancellationToken);
        }

        var destination = new Destination(
            "D", Destination, [new AllFilesFilter()], SyncStrategy.Mirror);
        var triggers = new FakeTriggerSourceFactory();
        var node = NewNode(new SyncTask("T", Source, new WatchTrigger(), [destination]), triggers);
        var source = triggers.Created.Single();

        var raisers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 10; i++)
                {
                    source.Raise();
                }
            }))
            .ToArray();

        await Task.WhenAll(raisers);
        await DrainAsync(node);

        AssertTreesMatch();
        Assert.Empty(
            Directory.EnumerateFiles(Destination, "*.syncmaid-tmp-*", SearchOption.AllDirectories));
    }

    // Mirror's contract: a file-tree compare of source and destination reports identical,
    // empty directories included.
    private void AssertTreesMatch()
    {
        Assert.Equal(RelativeEntries(Source), RelativeEntries(Destination));

        foreach (var relative in RelativeEntries(Source).Where(e => File.Exists(Path.Combine(Source, e))))
        {
            Assert.Equal(
                File.ReadAllText(Path.Combine(Source, relative)),
                File.ReadAllText(Path.Combine(Destination, relative)));
        }
    }

    private static List<string> RelativeEntries(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // A coalesced follow-up starts as the previous run finishes, so IsRunning can dip
    // false in between. Require it to stay false across several checks before trusting it,
    // or the assertions could read the tree mid-run and fail for the wrong reason.
    private static async Task DrainAsync(TaskNodeViewModel node)
    {
        const int requiredQuietChecks = 5;

        var quiet = 0;
        for (var attempt = 0; attempt < 300 && quiet < requiredQuietChecks; attempt++)
        {
            quiet = node.IsRunning ? 0 : quiet + 1;
            await Task.Delay(20);
        }

        Assert.True(quiet >= requiredQuietChecks, "A run was still in flight after the drain window.");
    }

    private static TaskNodeViewModel NewNode(SyncTask task, FakeTriggerSourceFactory triggers) =>
        new(
            task,
            new Dictionary<Guid, DestinationSyncStatus>(),
            new FakeDialogService(),
            new SyncEngine(new PhysicalFileSystem()),
            triggers,
            new FakeUiDispatcher(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => { },
            _ => { },
            NullLogger.Instance,
            new FakeMirrorDeleteConfirmer());
}
