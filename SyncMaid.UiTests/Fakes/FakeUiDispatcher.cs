using System;
using SyncMaid.Services;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Runs posted actions synchronously, so tests observe results immediately.</summary>
public sealed class FakeUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
