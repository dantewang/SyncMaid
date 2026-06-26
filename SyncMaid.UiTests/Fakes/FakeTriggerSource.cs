using System;
using SyncMaid.Core.Triggers;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// A trigger source whose firing tests control via <see cref="Raise"/>, and which
/// records whether it was started and disposed.
/// </summary>
public sealed class FakeTriggerSource : ITriggerSource
{
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler? Fired;

    public void Start() => Started = true;

    public void Stop() => Started = false;

    /// <summary>Simulates the trigger firing (a watch event, a schedule tick).</summary>
    public void Raise() => Fired?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
