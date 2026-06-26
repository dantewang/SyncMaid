namespace SyncMaid.ViewModels;

/// <summary>
/// The trigger choices shown in the task editor. A flat enum is convenient to bind to a
/// combo box; the editor maps it to the domain <see cref="Core.Triggers.Trigger"/>
/// hierarchy (and back) on save/load.
/// </summary>
public enum TaskTriggerType
{
    Manual,
    Scheduled,
    Watch,
}
