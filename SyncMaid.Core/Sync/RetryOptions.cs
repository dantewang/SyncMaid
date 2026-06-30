namespace SyncMaid.Core.Sync;

/// <summary>
/// How the engine retries a transient I/O failure (a momentarily locked file — antivirus
/// scanning, another process holding a handle, a brief sharing violation) before giving up
/// on a destination. Backoff is linear: <c>BaseDelay × attempt</c>.
/// </summary>
public sealed record RetryOptions(int MaxAttempts, TimeSpan BaseDelay)
{
    /// <summary>Three attempts with a short backoff — the production default.</summary>
    public static RetryOptions Default { get; } = new(MaxAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(200));

    /// <summary>A single attempt, no delay — for tests and callers that don't want retries.</summary>
    public static RetryOptions None { get; } = new(MaxAttempts: 1, BaseDelay: TimeSpan.Zero);
}
