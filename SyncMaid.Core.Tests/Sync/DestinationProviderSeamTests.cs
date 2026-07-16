using SyncMaid.Core.Filtering;
using SyncMaid.Core.IO;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Sync;

/// <summary>
/// Proves the destination-provider abstraction is a real seam: the engine drives whatever
/// provider the factory returns, so a non-filesystem backend (cloud/SFTP later) plugs in
/// without engine changes. Also covers the local factory's routing.
/// </summary>
public class DestinationProviderSeamTests
{
    private sealed record UnknownLocation : DestinationLocation;

    [Fact]
    public void Local_factory_creates_a_local_provider_and_rejects_unknown_kinds()
    {
        var factory = new LocalDestinationProviderFactory(new InMemoryFileSystem());

        Assert.IsType<LocalDestinationProvider>(factory.Create(new LocalDestination(@"D:\d")));
        Assert.Throws<NotSupportedException>(() => factory.Create(new UnknownLocation()));
    }

    [Fact]
    public async Task Engine_writes_through_whatever_provider_the_factory_returns()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(@"S:\src\a.txt", "a");
        fs.AddFile(@"S:\src\b.txt", "b");
        var provider = new RecordingProvider();

        var dest = new Destination("d", new LocalDestination(@"cloud://bucket"), [new AllFilesFilter()], SyncStrategy.AddOnly);
        var task = new SyncTask("t", @"S:\src", new ManualTrigger(), [dest]);

        var statuses = await new SyncEngine(fs, new StubFactory(provider), RetryOptions.None).ExecuteAsync(task);

        Assert.Equal(SyncOutcome.Success, Assert.Single(statuses).Outcome);
        Assert.Equal(new[] { "a.txt", "b.txt" }, provider.Written.OrderBy(p => p));
    }

    // A destination provider that records operations in memory instead of touching a
    // filesystem — stands in for a future cloud/SFTP provider.
    private sealed class RecordingProvider : IDestinationProvider
    {
        private readonly Dictionary<string, FileStamp> _files = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Written { get; } = [];
        public List<string> Deleted { get; } = [];

        public DestinationCapabilities Capabilities => new(IsRemote: true, SupportsRecycle: false);
        public IEnumerable<string> Enumerate() => _files.Keys;
        public IEnumerable<string> EnumerateDirectories() => [];
        public FileStamp GetStamp(string relativePath) =>
            _files.TryGetValue(relativePath, out var stamp)
                ? stamp
                : throw new FileNotFoundException("Destination file is missing.", relativePath);

        public bool TryGetStamp(string relativePath, out FileStamp stamp) =>
            _files.TryGetValue(relativePath, out stamp);

        public void Write(string relativePath, ISourceFile source, bool verifyContents)
        {
            Written.Add(relativePath);
            _files[relativePath] = source.Stamp;
        }

        public void Delete(string relativePath, DeleteMode mode)
        {
            Deleted.Add(relativePath);
            _files.Remove(relativePath);
        }

        public void EnsureDirectory(string relativePath)
        {
            // Nothing to do: this in-memory backend has no directories.
        }

        public void DeleteEmptyDirectory(string relativePath)
        {
            // Nothing to do: this in-memory backend has no directories.
        }
    }

    private sealed class StubFactory(IDestinationProvider provider) : IDestinationProviderFactory
    {
        public IDestinationProvider Create(DestinationLocation location) => provider;
    }
}
