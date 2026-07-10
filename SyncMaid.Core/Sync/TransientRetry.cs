namespace SyncMaid.Core.Sync;

/// <summary>
/// Runs an action with bounded retries for <b>transient</b> I/O only — a locked file or a
/// brief sharing violation that usually clears on its own. Deterministic failures
/// (verification mismatch, guard violation, bad argument) are not transient and propagate
/// on the first attempt, so a genuinely corrupt copy is never "retried into" success.
/// </summary>
public static class TransientRetry
{
    /// <summary>
    /// Invokes <paramref name="action"/>, retrying up to <paramref name="maxAttempts"/>
    /// times on a transient exception. <paramref name="beforeRetry"/> is called with the
    /// just-failed attempt number before each retry (e.g. to back off).
    /// </summary>
    public static void Execute(Action action, int maxAttempts, Action<int>? beforeRetry = null)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                action();
                return;
            }
            catch (Exception exception) when (attempt < maxAttempts && IsTransient(exception))
            {
                beforeRetry?.Invoke(attempt);
            }
        }
    }

    /// <summary>True for I/O failures that are worth retrying (locks, sharing violations,
    /// momentary access denials).</summary>
    public static bool IsTransient(Exception exception) =>
        exception is (IOException and not FileNotFoundException) or UnauthorizedAccessException;
}
