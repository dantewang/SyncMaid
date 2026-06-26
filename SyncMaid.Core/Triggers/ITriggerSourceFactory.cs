namespace SyncMaid.Core.Triggers;

/// <summary>
/// Creates a live <see cref="ITriggerSource"/> for a task's <see cref="Trigger"/>.
/// Abstracted so the app can depend on it (and tests can substitute a fake source
/// whose firing they control).
/// </summary>
public interface ITriggerSourceFactory
{
    /// <param name="trigger">The task's configured trigger.</param>
    /// <param name="sourcePath">The task's source path (needed by the watch trigger).</param>
    ITriggerSource Create(Trigger trigger, string sourcePath);
}
