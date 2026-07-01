using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Creates providers for local/mounted destinations, backed by a single
/// <see cref="IFileSystem"/>. When cloud/SFTP arrive, a composite factory routes those
/// kinds to their own providers and delegates <see cref="LocalDestination"/> here.
/// </summary>
public sealed class LocalDestinationProviderFactory : IDestinationProviderFactory
{
    private readonly IFileSystem _fileSystem;

    public LocalDestinationProviderFactory(IFileSystem fileSystem) => _fileSystem = fileSystem;

    public IDestinationProvider Create(DestinationLocation location) => location switch
    {
        LocalDestination local => new LocalDestinationProvider(_fileSystem, local.Path),
        _ => throw new NotSupportedException(
            $"No destination provider is registered for '{location.GetType().Name}'."),
    };
}
