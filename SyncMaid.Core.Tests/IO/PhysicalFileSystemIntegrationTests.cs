using System.Text;
using SyncMaid.Core.IO;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Sync;

namespace SyncMaid.Core.Tests.IO;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PhysicalFileSystemCollection
{
    public const string Name = "Physical filesystem integration";
}

[Collection(PhysicalFileSystemCollection.Name)]
public sealed class PhysicalFileSystemIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "syncmaid-physical-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _physical = new();

    public PhysicalFileSystemIntegrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CreateWriteThrough_replace_and_stamps_round_trip_on_disk()
    {
        var staged = Path.Combine(_root, "nested", "staged.tmp");
        var destination = Path.Combine(_root, "destination.bin");
        var contents = Encoding.UTF8.GetBytes("durable payload");
        var timestamp = new DateTime(2026, 7, 1, 12, 34, 56, DateTimeKind.Utc);

        using (var stream = _physical.CreateWriteThrough(staged))
        {
            stream.Write(contents);
            stream.Flush();
        }

        _physical.SetLastWriteTimeUtc(staged, timestamp);
        var stagedStamp = _physical.GetStamp(staged);
        _physical.Replace(staged, destination);

        Assert.False(_physical.FileExists(staged));
        Assert.Equal(contents, _physical.ReadAllBytes(destination));
        Assert.Equal(stagedStamp, _physical.GetStamp(destination));
    }

    [Fact]
    public void SafeFileTransfer_copy_round_trips_and_verifies_on_disk()
    {
        var source = Path.Combine(_root, "source.txt");
        var destination = Path.Combine(_root, "copy", "destination.txt");
        File.WriteAllText(source, "real disk contents");
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 7, 2, 1, 2, 4, DateTimeKind.Utc));

        SafeFileTransfer.Copy(_physical, source, destination, verifyContents: true);

        Assert.Equal("real disk contents", File.ReadAllText(destination));
        Assert.Equal(_physical.GetStamp(source), _physical.GetStamp(destination));
        Assert.True(_physical.FileExists(source));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.syncmaid-tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public void SafeFileTransfer_verification_failure_keeps_the_existing_disk_file()
    {
        var source = Path.Combine(_root, "source.txt");
        var destination = Path.Combine(_root, "destination.txt");
        File.WriteAllText(source, "new contents");
        File.WriteAllText(destination, "previous good copy");
        var fileSystem = new FaultingPhysicalFileSystem(_physical) { CorruptTempReadBack = true };

        Assert.Throws<SyncVerificationException>(() =>
            SafeFileTransfer.Copy(fileSystem, source, destination, verifyContents: true));

        Assert.Equal("previous good copy", File.ReadAllText(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.syncmaid-tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public void AtomicFile_failure_before_rename_keeps_the_original_disk_file()
    {
        var path = Path.Combine(_root, "tasks.json");
        File.WriteAllText(path, "original");
        var fileSystem = new FaultingPhysicalFileSystem(_physical) { FailFileExistsPath = path };

        Assert.Throws<IOException>(() =>
            AtomicFile.Write(fileSystem, path, Encoding.UTF8.GetBytes("replacement")));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp-*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FaultingPhysicalFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool CorruptTempReadBack { get; init; }
        public string? FailFileExistsPath { get; init; }

        public IEnumerable<string> EnumerateFiles(string root) => inner.EnumerateFiles(root);

        public bool FileExists(string path)
        {
            if (SamePath(path, FailFileExistsPath))
            {
                throw new IOException("Simulated failure before atomic rename.");
            }

            return inner.FileExists(path);
        }

        public FileStamp GetStamp(string path) => inner.GetStamp(path);
        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] contents) => inner.WriteAllBytes(path, contents);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void Recycle(string path) => inner.Recycle(path);
        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);

        public Stream OpenRead(string path)
        {
            if (!CorruptTempReadBack || !path.Contains(".syncmaid-tmp-", StringComparison.Ordinal))
            {
                return inner.OpenRead(path);
            }

            var contents = inner.ReadAllBytes(path);
            if (contents.Length > 0)
            {
                contents[0] ^= 0xFF;
            }

            return new MemoryStream(contents, writable: false);
        }

        public Stream CreateWriteThrough(string path) => inner.CreateWriteThrough(path);
        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) =>
            inner.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        public void Replace(string sourcePath, string destinationPath) => inner.Replace(sourcePath, destinationPath);
        public long GetAvailableFreeSpace(string path) => inner.GetAvailableFreeSpace(path);

        private static bool SamePath(string path, string? expected) =>
            expected is not null
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
    }
}
