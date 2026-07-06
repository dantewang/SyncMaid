using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;

namespace SyncMaid.Services;

/// <summary>What the user is being asked to approve — a Mirror mass-delete.</summary>
public sealed record MirrorDeleteRequest(
    string DestinationName,
    string DestinationPath,
    DeleteMode DeleteMode,
    MirrorDeletePreview Preview);

/// <summary>
/// Asks the user to confirm a blocked Mirror mass-delete. Implemented as an independent
/// top-level window (not an in-window overlay) so it works even when the main window is
/// hidden — important for a background/tray app: a background run that trips the guard must
/// not force the main window open.
/// </summary>
public interface IMirrorDeleteConfirmer
{
    /// <summary>Returns true if the user approved the deletion, false if they kept the files.</summary>
    Task<bool> ConfirmAsync(MirrorDeleteRequest request);
}
