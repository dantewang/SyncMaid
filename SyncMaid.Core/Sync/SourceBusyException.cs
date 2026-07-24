namespace SyncMaid.Core.Sync;

/// <summary>
/// Raised when a source file is still being written: its stamp no longer matches the one
/// the planner saw, so copying it now would capture a half-written state. Deliberately
/// <b>not</b> an <see cref="IOException"/> — it must not be retried
/// (<see cref="TransientRetry"/>), because waiting out a long save inside a run would
/// stall every operation behind it. The engine defers the file instead, and the next run
/// copies it once it settles.
/// </summary>
public sealed class SourceBusyException : Exception
{
    public SourceBusyException(string sourceFullPath)
        : base($"Source '{sourceFullPath}' is still being written; deferred to the next run.")
    {
        SourceFullPath = sourceFullPath;
    }

    /// <summary>The absolute source path that was still changing.</summary>
    public string SourceFullPath { get; }
}
