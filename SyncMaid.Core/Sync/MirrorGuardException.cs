namespace SyncMaid.Core.Sync;

/// <summary>
/// Thrown when a Mirror run's planned deletions look dangerous — the source is empty or
/// unavailable, or the run would delete a large fraction of the destination. The
/// destination is left untouched and the run is reported as failed so the user is alerted
/// rather than silently losing files.
/// </summary>
public sealed class MirrorGuardException : Exception
{
    public MirrorGuardException(string message) : base(message)
    {
    }
}
