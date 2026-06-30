namespace SyncMaid.Core.Sync;

/// <summary>
/// Thrown when a copied file fails verification before it is committed over the
/// destination (length mismatch, or — when content verification is enabled — an
/// xxHash mismatch between source and the written copy). The transfer leaves the
/// existing destination untouched; for a move, the source is not deleted.
/// </summary>
public sealed class SyncVerificationException : Exception
{
    public SyncVerificationException(string message) : base(message)
    {
    }
}
