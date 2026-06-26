using System.IO;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

public class SyncEngineStatusTests
{
    // Wraps the in-memory filesystem but fails copies to any path containing a marker,
    // so a single destination can be made to fail deterministically.
    private sealed class FaultyFileSystem(InMemoryFileSystem inner, string failMarker) : IFileSystem
    {
        public IEnumerable<string> EnumerateFiles(string root) => inner.EnumerateFiles(root);
        public bool FileExists(string path) => inner.FileExists(path);
        public FileStamp GetStamp(string path) => inner.GetStamp(path);
        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] contents) => inner.WriteAllBytes(path, contents);
        public void MoveFile(string source, string destination) => inner.MoveFile(source, destination);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);

        public void CopyFile(string source, string destination)
        {
            if (destination.Contains(failMarker))
            {
                throw new IOException("simulated copy failure");
            }

            inner.CopyFile(source, destination);
        }
    }

    [Fact]
    public async Task Returns_success_status_with_files_copied()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"C:\src\a.txt", "a");
        fs.AddFile(@"C:\src\b.txt", "b");
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", @"C:\src", new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(fs).ExecuteAsync(task);

        var status = Assert.Single(statuses);
        Assert.Equal(dest.Id, status.DestinationId);
        Assert.Equal(SyncOutcome.Success, status.Outcome);
        Assert.Equal(2, status.FilesCopied);
        Assert.NotNull(status.LastRun);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task A_failed_destination_is_captured_and_does_not_abort_the_others()
    {
        var inner = new InMemoryFileSystem();
        inner.AddFile(@"C:\src\a.txt", "a");
        var fs = new FaultyFileSystem(inner, failMarker: "bad");

        var good = new Destination("good", @"D:\good", [new AllFilesFilter()], SyncStrategy.Mirror);
        var bad = new Destination("bad", @"D:\bad", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", @"C:\src", new ManualTrigger(), [good, bad]);

        var statuses = await new SyncEngine(fs).ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, statuses.Single(s => s.DestinationId == good.Id).Outcome);
        var badStatus = statuses.Single(s => s.DestinationId == bad.Id);
        Assert.Equal(SyncOutcome.Failed, badStatus.Outcome);
        Assert.NotNull(badStatus.Error);
    }
}
