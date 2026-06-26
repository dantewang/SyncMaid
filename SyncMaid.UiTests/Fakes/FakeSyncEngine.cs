using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Records the tasks it was asked to run, without touching any filesystem.</summary>
public sealed class FakeSyncEngine : ISyncEngine
{
    public List<SyncTask> Executed { get; } = [];

    public Task ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null)
    {
        Executed.Add(task);
        return Task.CompletedTask;
    }
}
