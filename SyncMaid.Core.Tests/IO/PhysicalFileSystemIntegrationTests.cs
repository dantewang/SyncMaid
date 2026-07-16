using System.Text;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;

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

    // A missing root must be distinguishable from an empty one, or an unplugged source
    // drive reads as an empty source.
    [Fact]
    public void Enumerating_a_missing_root_throws_while_an_empty_one_yields_nothing()
    {
        var missing = Path.Combine(_root, "missing");
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        Assert.Throws<DirectoryNotFoundException>(() => _physical.EnumerateFiles(missing));
        Assert.Empty(_physical.EnumerateFiles(empty));
    }

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

    [Fact]
    public void EnumerateDirectories_yields_nested_relative_paths_and_throws_for_a_missing_root()
    {
        var root = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        File.WriteAllText(Path.Combine(root, "a", "file.txt"), "x");

        var directories = _physical.EnumerateDirectories(root).OrderBy(d => d).ToList();

        Assert.Equal(new[] { "a", "a/b", "empty" }, directories);
        Assert.Throws<DirectoryNotFoundException>(
            () => _physical.EnumerateDirectories(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void DeleteEmptyDirectory_removes_only_empty_directories_and_tolerates_missing_ones()
    {
        var empty = Path.Combine(_root, "empty");
        var full = Path.Combine(_root, "full");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "keep.txt"), "content");

        _physical.DeleteEmptyDirectory(empty);
        _physical.DeleteEmptyDirectory(full);
        _physical.DeleteEmptyDirectory(Path.Combine(_root, "missing"));

        Assert.False(Directory.Exists(empty));
        Assert.True(File.Exists(Path.Combine(full, "keep.txt")));
    }

    // Mirror's whole contract on real disk: after a run, a tree compare of source and
    // destination reports identical — files, folders, and empty folders alike.
    [Fact]
    public async Task Mirror_run_makes_source_and_destination_trees_identical_on_disk()
    {
        var source = Path.Combine(_root, "src");
        var destination = Path.Combine(_root, "dst");
        Directory.CreateDirectory(Path.Combine(source, "item1"));
        Directory.CreateDirectory(Path.Combine(source, "kept-empty"));
        File.WriteAllText(Path.Combine(source, "item1", "photo.png"), "img");
        // Stale destination content: an orphaned tree that must disappear entirely.
        Directory.CreateDirectory(Path.Combine(destination, "removed", "nested"));
        File.WriteAllText(Path.Combine(destination, "removed", "nested", "old.txt"), "stale");

        var dest = new Destination("d", destination, [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            DeleteMode = DeleteMode.Permanent,
        };
        var task = new SyncTask("t", source, new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(_physical).ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.Equal(RelativeTree(source), RelativeTree(destination));
        Assert.True(Directory.Exists(Path.Combine(destination, "kept-empty")));
        Assert.False(Directory.Exists(Path.Combine(destination, "removed")));
    }

    private IReadOnlyList<string> RelativeTree(string root) =>
        _physical.EnumerateDirectories(root).Select(directory => "dir:" + directory)
            .Concat(_physical.EnumerateFiles(root).Select(file => "file:" + file))
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToList();

    [Fact]
    public void Recycle_path_is_absolute_and_uses_Win32_separators()
    {
        var mixedPath = Path.Combine(_root, "nested", "file.txt").Replace('\\', '/');

        var normalized = PhysicalFileSystem.NormalizeRecyclePath(mixedPath);

        Assert.Equal(Path.GetFullPath(mixedPath).Replace('/', '\\'), normalized);
        Assert.DoesNotContain('/', normalized);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Test cleanup is best effort; a delayed filesystem handle must not mask the assertion result.
        }
    }

    private sealed class FaultingPhysicalFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool CorruptTempReadBack { get; init; }
        public string? FailFileExistsPath { get; init; }

        public IEnumerable<string> EnumerateFiles(string root) => inner.EnumerateFiles(root);
        public IEnumerable<string> EnumerateDirectories(string root) => inner.EnumerateDirectories(root);

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
        public void DeleteEmptyDirectory(string path) => inner.DeleteEmptyDirectory(path);

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
