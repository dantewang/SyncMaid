using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// Resolves the <see cref="IDestinationProvider"/> for a <see cref="DestinationLocation"/>.
/// This is the extension seam: supporting a new backend means adding a
/// <see cref="DestinationLocation"/> kind and a provider, then routing it here — the engine
/// is untouched.
/// </summary>
public interface IDestinationProviderFactory
{
    /// <summary>Creates a provider bound to <paramref name="location"/>. Throws for an unsupported kind.</summary>
    IDestinationProvider Create(DestinationLocation location);
}
