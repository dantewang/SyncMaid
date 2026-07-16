using SyncMaid.Core.IO;
using SyncMaid.Core.Model;

namespace SyncMaid.Core.Sync;

/// <summary>
/// What a destination can and cannot do, so the engine and UI can adapt (e.g. warn that a
/// backend can't recycle). Extended as cloud/SFTP arrive.
/// </summary>
/// <param name="IsRemote">True for API-only backends (cloud); false for OS filesystem paths (local/mounted).</param>
/// <param name="SupportsRecycle">True when deletions can go to a Recycle Bin rather than being permanent.</param>
public readonly record struct DestinationCapabilities(bool IsRemote, bool SupportsRecycle);

/// <summary>
/// A place files are synced to, addressed by paths relative to its root. Each provider owns
/// its own commit and verification strategy (see the location-and-verification design), so
/// the engine never assumes the destination is the same filesystem as the source. A provider
/// instance is bound to one destination location.
/// </summary>
/// <remarks>
/// Implementations are trim/AOT-safe. Today the only implementation is
/// <see cref="LocalDestinationProvider"/>; cloud/SFTP providers implement this same contract.
/// </remarks>
public interface IDestinationProvider
{
    /// <summary>What this destination supports (see <see cref="DestinationCapabilities"/>).</summary>
    DestinationCapabilities Capabilities { get; }

    /// <summary>Enumerates existing files under the destination as relative paths (forward slashes).</summary>
    IEnumerable<string> Enumerate();

    /// <summary>Enumerates existing directories under the destination as relative paths
    /// (forward slashes, the root itself excluded). A destination that does not exist yet
    /// has no directories.</summary>
    IEnumerable<string> EnumerateDirectories();

    /// <summary>The change-detection stamp of the destination file at <paramref name="relativePath"/>.
    /// Throws <see cref="FileNotFoundException"/> when the file does not exist.</summary>
    FileStamp GetStamp(string relativePath);

    /// <summary>Attempts to read the destination stamp without using an exception to report
    /// that <paramref name="relativePath"/> does not exist.</summary>
    bool TryGetStamp(string relativePath, out FileStamp stamp);

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="relativePath"/>, committing
    /// atomically and verifying (length always; contents when <paramref name="verifyContents"/>)
    /// before the destination is replaced. Throws on failure, leaving any existing file intact.
    /// </summary>
    void Write(string relativePath, ISourceFile source, bool verifyContents);

    /// <summary>Deletes the destination file at <paramref name="relativePath"/> per <paramref name="mode"/>.</summary>
    void Delete(string relativePath, DeleteMode mode);

    /// <summary>Ensures the destination directory at <paramref name="relativePath"/> exists
    /// (parents included), so Mirror can replicate directories that hold no files.</summary>
    void EnsureDirectory(string relativePath);

    /// <summary>
    /// Removes the destination directory at <paramref name="relativePath"/> only if it is
    /// empty. Best-effort: a directory that has content (planning raced new writes) or no
    /// longer exists is left alone without error, so unknown content is never deleted.
    /// </summary>
    void DeleteEmptyDirectory(string relativePath);
}
