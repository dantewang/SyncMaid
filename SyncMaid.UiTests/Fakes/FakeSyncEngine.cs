using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;

namespace SyncMaid.UiTests.Fakes;

/// <summary>
/// Records the tasks it was asked to run, without touching any filesystem. Returns a
/// success status for each destination by default; set <see cref="Result"/> to override.
/// </summary>
public sealed class FakeSyncEngine : ISyncEngine
{
    public List<SyncTask> Executed { get; } = [];

    /// <summary>When set, returned from the next run instead of the default successes.</summary>
    public IReadOnlyList<DestinationSyncStatus>? Result { get; set; }

    public Task<IReadOnlyList<DestinationSyncStatus>> ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null)
    {
        Executed.Add(task);

        IReadOnlyList<DestinationSyncStatus> statuses = Result ?? task.Destinations
            .Select(d => new DestinationSyncStatus(d.Id, SyncOutcome.Success, DateTimeOffset.UtcNow, 1, null))
            .ToList();

        return Task.FromResult(statuses);
    }
}
