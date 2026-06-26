namespace SyncMaid.Core.Triggers;

/// <summary>
/// A live source that raises <see cref="Fired"/> when its task should run. One per
/// active task; created from the task's <see cref="Trigger"/> by
/// <see cref="TriggerSourceFactory"/>. The consumer (the app) subscribes to
/// <see cref="Fired"/> and runs the sync engine in response.
/// </summary>
public interface ITriggerSource : IDisposable
{
    /// <summary>Raised when the task should run now.</summary>
    event EventHandler? Fired;

    /// <summary>Begins watching/scheduling. No-op for a manual trigger.</summary>
    void Start();

    /// <summary>Stops watching/scheduling without disposing; <see cref="Start"/> can resume.</summary>
    void Stop();
}
