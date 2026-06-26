using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Tests.IO;

namespace SyncMaid.Core.Tests.Persistence;

public class JsonStatusStoreTests
{
    private const string Path = @"C:\config\status.json";

    [Fact]
    public void Load_returns_empty_when_no_file_exists()
    {
        Assert.Empty(new JsonStatusStore(new InMemoryFileSystem(), Path).Load());
    }

    [Fact]
    public void Round_trips_statuses_keyed_by_destination_id()
    {
        var fs = new InMemoryFileSystem();
        var store = new JsonStatusStore(fs, Path);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        store.Save(new Dictionary<Guid, DestinationSyncStatus>
        {
            [id1] = new(id1, SyncOutcome.Success, DateTimeOffset.Parse("2026-01-01T08:00:00Z"), 5, null),
            [id2] = new(id2, SyncOutcome.Failed, DateTimeOffset.Parse("2026-02-02T09:30:00Z"), 0, "folder not found"),
        });

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(SyncOutcome.Success, loaded[id1].Outcome);
        Assert.Equal(5, loaded[id1].FilesCopied);
        Assert.Equal("folder not found", loaded[id2].Error);
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T09:30:00Z"), loaded[id2].LastRun);
    }
}
