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
    public event Action<Exception>? Error;
    public event Action? Recovered;

    public void Start() => Started = true;

    public void Stop() => Started = false;

    /// <summary>Simulates the trigger firing (a watch event, a schedule tick).</summary>
    public void Raise() => Fired?.Invoke(this, EventArgs.Empty);

    /// <summary>Simulates a background failure in the trigger source.</summary>
    public void RaiseError(Exception exception) => Error?.Invoke(exception);

    /// <summary>Simulates a recoverable trigger returning to healthy operation.</summary>
    public void RaiseRecovered() => Recovered?.Invoke();

    public void Dispose() => Disposed = true;
}
