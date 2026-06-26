namespace SyncMaid.Core.Model;

/// <summary>How a destination is reconciled with the filtered source. One per destination.</summary>
public enum SyncStrategy
{
    /// <summary>Keep the destination identical to the filtered source: copy new/changed files and delete extras.</summary>
    Mirror,

    /// <summary>Copy new and changed files only; never delete from the destination.</summary>
    AddOnly,

    /// <summary>Move filtered files from the source to the destination, removing them from the source.</summary>
    Move
}
