using System;
using Avalonia.Threading;

namespace SyncMaid.Services;

/// <summary>Posts actions to Avalonia's UI thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
