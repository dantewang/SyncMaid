namespace SyncMaid.Core.Model;

/// <summary>How a Mirror destination removes files that are no longer in the source set.</summary>
public enum DeleteMode
{
    /// <summary>Send removed files to the Recycle Bin so they are recoverable (the safe default).
    /// On volumes without a Recycle Bin (e.g. network shares) this falls back to a permanent delete.</summary>
    Recycle,

    /// <summary>Delete removed files permanently.</summary>
    Permanent,
}
