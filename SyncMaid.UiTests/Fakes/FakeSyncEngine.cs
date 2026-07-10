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

    /// <summary>Progress updates reported (in order) before the run completes.</summary>
    public IReadOnlyList<SyncProgress>? ProgressToReport { get; set; }

    /// <summary>When true, the run blocks until cancelled (then throws), simulating a long sync.</summary>
    public bool HangUntilCancelled { get; set; }

    /// <summary>When true, destinations return NeedsConfirmation unless their id was confirmed.</summary>
    public bool NeedsConfirmation { get; set; }

    /// <summary>When set, the run throws this unexpected failure after it is recorded.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>The confirmed-mass-delete set passed to the most recent run.</summary>
    public IReadOnlySet<Guid>? LastConfirmed { get; private set; }

    /// <summary>Returned from <see cref="PreviewMirrorDeletionsAsync"/>.</summary>
    public MirrorDeletePreview PreviewResult { get; set; } = MirrorDeletePreview.None;

    public async Task<IReadOnlyList<DestinationSyncStatus>> ExecuteAsync(
        SyncTask task,
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null,
        IReadOnlySet<Guid>? confirmedMassDeletes = null)
    {
        Executed.Add(task);
        LastConfirmed = confirmedMassDeletes;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (progress is not null && ProgressToReport is not null)
        {
            foreach (var report in ProgressToReport)
            {
                progress.Report(report);
            }
        }

        if (HangUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken); // throws OperationCanceledException on cancel
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Result is not null)
        {
            return Result;
        }

        return task.Destinations
            .Select(d =>
            {
                var confirmed = confirmedMassDeletes?.Contains(d.Id) ?? false;
                var outcome = NeedsConfirmation && !confirmed ? SyncOutcome.NeedsConfirmation : SyncOutcome.Success;
                return new DestinationSyncStatus(d.Id, outcome, DateTimeOffset.UtcNow,
                    outcome == SyncOutcome.Success ? 1 : 0, null);
            })
            .ToList();
    }

    public Task<MirrorDeletePreview> PreviewMirrorDeletionsAsync(
        SyncTask task, Guid destinationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(PreviewResult);
}
