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
    private int _activeExecutions;
    private int _maxConcurrentExecutions;

    public List<SyncTask> Executed { get; } = [];

    /// <summary>When set, returned from the next run instead of the default successes.</summary>
    public IReadOnlyList<DestinationSyncStatus>? Result { get; set; }

    /// <summary>Progress updates reported (in order) before the run completes.</summary>
    public IReadOnlyList<SyncProgress>? ProgressToReport { get; set; }

    /// <summary>When true, the run blocks until cancelled (then throws), simulating a long sync.</summary>
    public bool HangUntilCancelled { get; set; }

    /// <summary>
    /// When true, destinations return NeedsConfirmation unless their id was confirmed.
    /// This models only the overridable mass-delete verdict from
    /// <see cref="SyncEngine.ExecuteAsync"/>. The production empty-source guard returns
    /// <see cref="SyncOutcome.Failed"/> instead; tests for that contract should set
    /// <see cref="Result"/> explicitly.
    /// </summary>
    public bool NeedsConfirmation { get; set; }

    /// <summary>When set, the run throws this unexpected failure after it is recorded.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>When set, executions wait here until the test releases the gate.</summary>
    public Task? ExecutionGate { get; set; }

    /// <summary>When set, cancellation waits here before the fake exits the active run.</summary>
    public Task? CancellationExitGate { get; set; }

    public CancellationToken LastCancellationToken { get; private set; }
    public int MaxConcurrentExecutions => Volatile.Read(ref _maxConcurrentExecutions);

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
        LastCancellationToken = cancellationToken;
        var active = Interlocked.Increment(ref _activeExecutions);
        var observedMaximum = Volatile.Read(ref _maxConcurrentExecutions);
        while (active > observedMaximum)
        {
            var original = Interlocked.CompareExchange(
                ref _maxConcurrentExecutions, active, observedMaximum);
            if (original == observedMaximum)
            {
                break;
            }

            observedMaximum = original;
        }

        try
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            try
            {
                if (ExecutionGate is not null)
                {
                    await ExecutionGate.WaitAsync(cancellationToken);
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
                    await Task.Delay(Timeout.Infinite, cancellationToken); // throws on cancel
                }
            }
            catch (OperationCanceledException) when (CancellationExitGate is not null)
            {
                await CancellationExitGate;
                throw;
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
        finally
        {
            Interlocked.Decrement(ref _activeExecutions);
        }
    }

    public Task<MirrorDeletePreview> PreviewMirrorDeletionsAsync(
        SyncTask task, Guid destinationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(PreviewResult);
}
