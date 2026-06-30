using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

public class SyncEngineRetryTests
{
    [Fact]
    public async Task A_transiently_locked_file_is_retried_and_then_succeeds()
    {
        var fs = new InMemoryFileSystem { FailWritesTimes = 2 }; // clears after two retries
        fs.AddFile(@"S:\src\a.txt", "a");
        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.AddOnly);
        var task = new SyncTask("t", @"S:\src", new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(fs, new RetryOptions(MaxAttempts: 3, BaseDelay: TimeSpan.Zero))
            .ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.True(fs.FileExists(@"D:\dst\a.txt"));
    }

    [Fact]
    public async Task A_persistently_locked_file_fails_the_destination_after_retries()
    {
        var fs = new InMemoryFileSystem { FailWrites = true }; // never clears
        fs.AddFile(@"S:\src\a.txt", "a");
        var dest = new Destination("d", @"D:\dst", [new AllFilesFilter()], SyncStrategy.AddOnly);
        var task = new SyncTask("t", @"S:\src", new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(fs, new RetryOptions(MaxAttempts: 3, BaseDelay: TimeSpan.Zero))
            .ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Failed, Assert.Single(statuses).Outcome);
        Assert.False(fs.FileExists(@"D:\dst\a.txt"));
    }
}
