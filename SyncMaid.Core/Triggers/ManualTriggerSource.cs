namespace SyncMaid.Core.Triggers;

/// <summary>
/// A trigger source for manual tasks. It never fires on its own — the user runs the
/// task explicitly — but exists so every <see cref="Trigger"/> maps to an
/// <see cref="ITriggerSource"/> uniformly. Call <see cref="Fire"/> to run on demand.
/// </summary>
public sealed class ManualTriggerSource : ITriggerSource
{
    /// <inheritdoc />
    public event EventHandler? Fired;

    /// <summary>Raises <see cref="Fired"/> immediately (e.g. from a "Run now" button).</summary>
    public void Fire() => Fired?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Start()
    {
        // Nothing to schedule; manual tasks fire only via Fire().
    }

    /// <inheritdoc />
    public void Stop()
    {
        // Nothing to stop.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No resources held.
    }
}
