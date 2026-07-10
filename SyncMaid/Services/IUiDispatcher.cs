using System;
using System.Threading.Tasks;

namespace SyncMaid.Services;

/// <summary>
/// Marshals an action onto the UI thread. View models use this when updating observable
/// state from background work (e.g. a sync started by a timer/watcher trigger), without
/// taking a direct dependency on Avalonia's dispatcher (so they stay unit-testable).
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);

    /// <summary>Runs a value-producing action on the UI thread and completes with its result.</summary>
    Task<T> InvokeAsync<T>(Func<T> action);
}
