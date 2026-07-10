using System;
using System.Threading.Tasks;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// Runs all work synchronously on the calling thread so tests observe results immediately.
/// This fake verifies dispatch calls, not thread safety: it cannot reproduce cross-thread UI races.
/// </summary>
public sealed class FakeUiDispatcher : IUiDispatcher
{
    public int InvokeCount { get; private set; }
    public Exception? InvokeException { get; set; }

    public void Post(Action action) => action();

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        InvokeCount++;
        if (InvokeException is not null)
        {
            throw InvokeException;
        }

        return Task.FromResult(action());
    }
}
