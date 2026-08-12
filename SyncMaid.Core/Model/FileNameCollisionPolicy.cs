namespace SyncMaid.Core.Model;

/// <summary>
/// What a flattening Move destination does when the file it is about to move already has a
/// namesake there. Only flattening can produce this: keeping the source structure gives every
/// source file a distinct destination path by construction.
/// </summary>
public enum FileNameCollisionPolicy
{
    /// <summary>Leave the file in the source and report it, so nothing is invented and no
    /// existing file is touched. The user decides what the duplicate is.</summary>
    Skip,

    /// <summary>Move it under a numbered name — <c>report (2).pdf</c> — so the run completes
    /// and both files survive.</summary>
    Suffix
}
