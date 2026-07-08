namespace SyncMaid.Core.Sync;

/// <summary>
/// Wraps a failure applying a single <see cref="SyncOperation"/>, annotating it with which
/// file/operation failed so a destination's stored error names the culprit
/// (e.g. "Failed to copy 'photos/img.jpg': …") instead of a bare, context-free message.
/// </summary>
public sealed class SyncOperationException : Exception
{
    public SyncOperationException(SyncOperation operation, Exception inner)
        : base($"{Describe(operation)}: {inner.Message}", inner)
    {
        Operation = operation;
    }

    /// <summary>The operation that failed.</summary>
    public SyncOperation Operation { get; }

    private static string Describe(SyncOperation operation) => operation switch
    {
        CopyOperation => $"Failed to copy '{operation.RelativePath}'",
        MoveOperation => $"Failed to move '{operation.RelativePath}'",
        DeleteOperation => $"Failed to delete '{operation.RelativePath}'",
        _ => $"Failed on '{operation.RelativePath}'",
    };
}
