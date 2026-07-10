using System;
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
    // Wraps the in-memory filesystem but fails writes to any path containing a marker,
    // so a single destination can be made to fail deterministically. The fault is on the
    // write path SafeFileTransfer actually uses (temp files live in the destination
    // directory, so they carry the marker too). The marker must be a token that appears in
    // the failing destination's path yet cannot occur by chance in the random temp name:
    // SafeFileTransfer's temp suffix is a hex GUID, so a marker built only from hex digits
    // (e.g. "bad") can spuriously match a temp file under the *good* destination. "nope"
    // includes non-hex letters, so it can't. (A backslash-delimited marker like @"\bad\"
    // fails the opposite way — LocalDestinationProvider joins with a forward slash, e.g.
    // D:\nope/a.txt, so it would never match at all.)
    private sealed class FaultyFileSystem(InMemoryFileSystem inner, string failMarker) : IFileSystem
    {
        public IEnumerable<string> EnumerateFiles(string root) => inner.EnumerateFiles(root);
        public bool FileExists(string path) => inner.FileExists(path);
        public FileStamp GetStamp(string path) => inner.GetStamp(path);
        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] contents) => inner.WriteAllBytes(path, contents);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void Recycle(string path) => inner.Recycle(path);
        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => inner.SetLastWriteTimeUtc(path, utc);
        public void Replace(string source, string destination) => inner.Replace(source, destination);
        public long GetAvailableFreeSpace(string path) => inner.GetAvailableFreeSpace(path);

        public Stream CreateWriteThrough(string path)
        {
            if (path.Contains(failMarker))
            {
                throw new IOException("simulated copy failure");
            }

            return inner.CreateWriteThrough(path);
        }
    }

    private sealed class CountingFileSystem(InMemoryFileSystem inner) : IFileSystem
    {
        private readonly Dictionary<string, int> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public int FileExistsCalls { get; private set; }

        public int EnumerationCount(string root) => _enumerations.GetValueOrDefault(root);

        public IEnumerable<string> EnumerateFiles(string root)
        {
            _enumerations[root] = EnumerationCount(root) + 1;
            foreach (var relativePath in inner.EnumerateFiles(root))
            {
                yield return relativePath;
            }
        }

        public bool FileExists(string path)
        {
            FileExistsCalls++;
            return inner.FileExists(path);
        }

        public FileStamp GetStamp(string path) => inner.GetStamp(path);
        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] contents) => inner.WriteAllBytes(path, contents);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void Recycle(string path) => inner.Recycle(path);
        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public Stream CreateWriteThrough(string path) => inner.CreateWriteThrough(path);
        public void SetLastWriteTimeUtc(string path, DateTime utc) => inner.SetLastWriteTimeUtc(path, utc);
        public void Replace(string source, string destination) => inner.Replace(source, destination);
        public long GetAvailableFreeSpace(string path) => inner.GetAvailableFreeSpace(path);
    }

    [Fact]
    public async Task Returns_success_status_with_files_copied()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"C:\src\a.txt", "a");
        fs.AddFile(@"C:\src\b.txt", "b");
        var dest = new Destination("D", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", @"C:\src", new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

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
        var fs = new FaultyFileSystem(inner, failMarker: "nope");

        var good = new Destination("good", @"D:\good", [new AllFilesFilter()], SyncStrategy.Mirror);
        var bad = new Destination("bad", @"D:\nope", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("T", @"C:\src", new ManualTrigger(), [good, bad]);

        var statuses = await new SyncEngine(fs, RetryOptions.None).ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, statuses.Single(s => s.DestinationId == good.Id).Outcome);
        var badStatus = statuses.Single(s => s.DestinationId == bad.Id);
        Assert.Equal(SyncOutcome.Failed, badStatus.Outcome);
        Assert.NotNull(badStatus.Error);
        Assert.Contains("a.txt", badStatus.Error!);   // the error names the file that failed (#9)
        Assert.Contains("copy", badStatus.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_run_walks_the_source_once_and_each_mirror_destination_once()
    {
        var inner = new InMemoryFileSystem();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        inner.AddFile(@"C:\src\a.txt", "a", stamp);
        inner.AddFile(@"C:\src\b.txt", "b", stamp);
        foreach (var root in new[] { @"D:\one", @"D:\two" })
        {
            inner.AddFile($@"{root}\a.txt", "a", stamp);
            inner.AddFile($@"{root}\b.txt", "b", stamp);
            inner.AddFile($@"{root}\orphan.txt", "old", stamp);
        }

        var fileSystem = new CountingFileSystem(inner);
        var destinations = new[]
        {
            new Destination("one", @"D:\one", [new AllFilesFilter()], SyncStrategy.Mirror)
            {
                MassDeleteThreshold = 0.9,
                DeleteMode = DeleteMode.Permanent,
            },
            new Destination("two", @"D:\two", [new AllFilesFilter()], SyncStrategy.Mirror)
            {
                MassDeleteThreshold = 0.9,
                DeleteMode = DeleteMode.Permanent,
            },
        };
        var task = new SyncTask("T", @"C:\src", new ManualTrigger(), destinations);

        var statuses = await new SyncEngine(fileSystem, RetryOptions.None).ExecuteAsync(task);

        Assert.All(statuses, status => Assert.Equal(SyncOutcome.Success, status.Outcome));
        Assert.Equal(1, fileSystem.EnumerationCount(@"C:\src"));
        Assert.Equal(1, fileSystem.EnumerationCount(@"D:\one"));
        Assert.Equal(1, fileSystem.EnumerationCount(@"D:\two"));
        Assert.Equal(0, fileSystem.FileExistsCalls);
        Assert.False(inner.FileExists(@"D:\one\orphan.txt"));
        Assert.False(inner.FileExists(@"D:\two\orphan.txt"));
    }
}
