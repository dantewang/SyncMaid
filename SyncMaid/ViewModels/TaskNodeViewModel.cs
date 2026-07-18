using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.Logging;
using SyncMaid.Core.Model;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
using SyncMaid.Lang;
using SyncMaid.Services;

namespace SyncMaid.ViewModels;

public partial class TaskNodeViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly ISyncEngine _engine;
    private readonly ITriggerSourceFactory _triggerFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<TaskNodeViewModel, Task> _onEdit;
    private readonly Func<TaskNodeViewModel, Task> _onDelete;
    private readonly Action _onChanged;
    private readonly Action<IReadOnlyList<DestinationSyncStatus>> _onStatusesUpdated;
    private readonly ILogger _logger;
    private readonly IMirrorDeleteConfirmer _confirmer;
    private readonly Func<string, string?> _destinationConflicts;
    private readonly Func<SyncTask, string?>? _runOverlapConflict;
    private readonly Lock _runGate = new();

    // Distinct files copied per destination across the current coalesced burst — only the
    // single-flight drain loop touches it. See DrainRunsAsync/AccumulateBurstCopies.
    private readonly Dictionary<Guid, HashSet<string>> _burstCopied = new();

    private ITriggerSource? _triggerSource;
    private CancellationTokenSource? _cts;
    private bool _runActive;
    private bool _hasPendingRun;
    private bool _acceptRunRequests = true;
    private bool _cancelUntilIdle;
    private IReadOnlySet<Guid>? _pendingConfirmedMassDeletes;
    private TaskCompletionSource? _runCompletion;

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>True while a sync is running — the view swaps the Run button for a Stop button.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Set when the task's trigger could not be started (e.g. a missing watch
    /// directory or bad cron); drives an amber "Trigger error" badge on the card. Null when
    /// the trigger is healthy. Holds the user-facing explanation, shown as the badge tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTriggerError))]
    private string? _triggerError;

    public TaskNodeViewModel(
        SyncTask task,
        IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses,
        IDialogService dialogs,
        ISyncEngine engine,
        ITriggerSourceFactory triggerFactory,
        IUiDispatcher dispatcher,
        Func<TaskNodeViewModel, Task> onEdit,
        Func<TaskNodeViewModel, Task> onDelete,
        Action onChanged,
        Action<IReadOnlyList<DestinationSyncStatus>> onStatusesUpdated,
        ILogger logger,
        IMirrorDeleteConfirmer confirmer,
        Func<string, string?>? destinationConflicts = null,
        Func<SyncTask, string?>? runOverlapConflict = null)
    {
        Task = task;
        _dialogs = dialogs;
        _engine = engine;
        _triggerFactory = triggerFactory;
        _dispatcher = dispatcher;
        _onEdit = onEdit;
        _onDelete = onDelete;
        _onChanged = onChanged;
        _onStatusesUpdated = onStatusesUpdated;
        _logger = logger;
        _confirmer = confirmer;
        _destinationConflicts = destinationConflicts ?? (_ => null);
        _runOverlapConflict = runOverlapConflict;

        Children = new ObservableCollection<DestinationNodeViewModel>();
        foreach (var destination in task.Destinations)
        {
            var status = statuses.TryGetValue(destination.Id, out var saved)
                ? saved
                : DestinationSyncStatus.Never(destination.Id);
            Children.Add(NewChild(destination, status));
        }

        Children.CollectionChanged += (_, _) =>
        {
            ExecuteCommand.NotifyCanExecuteChanged();
            AddDestinationCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AddDestinationHint));
            RefreshHealth();
        };

        StartTrigger();
        RefreshNextRun();
    }

    /// <summary>The current task. Replaced (immutably) whenever its destinations change.</summary>
    public SyncTask Task { get; private set; }

    // From the immutable task; the node is replaced on edit, so no notification needed.
    public string Name => Task.Name;
    public string Path => Task.SourcePath;

    /// <summary>Full name + path, shown as the sidebar tooltip since the row truncates both.</summary>
    public string SidebarTooltip => $"{Name}\n{Path}";

    public string TriggerText => Task.Trigger switch
    {
        ManualTrigger => Strings.Task_TriggerManual,
        WatchTrigger => Strings.Task_TriggerWatching,
        ScheduledTrigger scheduled => Localizer.Format(Strings.Task_TriggerScheduledFormat, scheduled.CronExpression),
        _ => Strings.Task_TriggerManual,
    };

    public MaterialIconKind TriggerIconKind => Task.Trigger switch
    {
        WatchTrigger => MaterialIconKind.Eye,
        ScheduledTrigger => MaterialIconKind.ClockOutline,
        _ => MaterialIconKind.CursorDefaultClickOutline,
    };

    public ObservableCollection<DestinationNodeViewModel> Children { get; }

    /// <summary>True when the trigger failed to start; drives the visibility of the error badge.</summary>
    public bool HasTriggerError => !string.IsNullOrEmpty(TriggerError);

    // The next scheduled fire time (UTC), recomputed by RefreshNextRun; null for non-scheduled
    // tasks or an expression with no future occurrence.
    private DateTimeOffset? _nextRun;

    /// <summary>True when this is a scheduled task with a known next run; drives the badge.</summary>
    public bool HasNextRun => _nextRun is not null;

    /// <summary>Relative next-run label for the badge, e.g. "next run in 2 h".</summary>
    public string NextRunText => _nextRun is { } when
        ? Localizer.Format(Strings.Task_NextRunFormat, Humanize(when - DateTimeOffset.UtcNow))
        : string.Empty;

    /// <summary>Absolute local next-run time, shown as the badge tooltip (never goes stale).</summary>
    public string? NextRunTooltip => _nextRun?.ToLocalTime().ToString("f");

    /// <summary>Recomputes the next scheduled run; called at construction and on a UI timer so the
    /// relative label stays current.</summary>
    public void RefreshNextRun()
    {
        _nextRun = Task.Trigger is ScheduledTrigger scheduled
                   && CronSchedule.NextOccurrenceUtc(scheduled.CronExpression, DateTime.UtcNow) is { } next
            ? new DateTimeOffset(next, TimeSpan.Zero)
            : null;

        OnPropertyChanged(nameof(HasNextRun));
        OnPropertyChanged(nameof(NextRunText));
        OnPropertyChanged(nameof(NextRunTooltip));
    }

    private static string Humanize(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return Strings.Time_DueNow;
        if (span < TimeSpan.FromMinutes(1)) return Strings.Time_InUnderAMinute;
        if (span < TimeSpan.FromHours(1)) return Localizer.Format(Strings.Time_InMinutesFormat, (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return Localizer.Format(Strings.Time_InHoursFormat, (int)span.TotalHours);
        return Localizer.Format(Strings.Time_InDaysFormat, (int)span.TotalDays);
    }

    /// <summary>At-a-glance health shown on the (possibly collapsed) card header.</summary>
    public SyncOutcome HealthOutcome
    {
        get
        {
            if (Children.Count == 0) return SyncOutcome.Never;
            if (Children.Any(c => c.Outcome == SyncOutcome.Running)) return SyncOutcome.Running;
            if (Children.Any(c => c.Outcome == SyncOutcome.NeedsConfirmation)) return SyncOutcome.NeedsConfirmation;
            if (Children.Any(c => c.Outcome == SyncOutcome.Failed)) return SyncOutcome.Failed;
            if (Children.Any(c => c.Outcome == SyncOutcome.Success)) return SyncOutcome.Success;
            return SyncOutcome.Never;
        }
    }

    public string HealthText
    {
        get
        {
            if (Children.Count == 0) return Strings.Task_HealthNoDestinations;
            if (Children.Any(c => c.Outcome == SyncOutcome.Running)) return Strings.Status_Syncing;
            if (Children.Any(c => c.Outcome == SyncOutcome.NeedsConfirmation)) return Strings.Status_NeedsConfirmation;
            var failed = Children.Count(c => c.Outcome == SyncOutcome.Failed);
            if (failed > 0) return Localizer.Format(Strings.Task_HealthFailedFormat, failed, Children.Count);
            if (Children.All(c => c.Outcome == SyncOutcome.Success)) return Strings.Task_HealthAllSynced;
            if (Children.Any(c => c.Outcome == SyncOutcome.Success)) return Strings.Task_HealthPartlySynced;
            return Strings.Status_NeverRun;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private Task Execute() => RunAsync();

    private bool CanExecute() => Children.Count > 0;

    /// <summary>Requests cancellation of the in-flight run (the Stop button).</summary>
    [RelayCommand]
    private void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_runGate)
        {
            if (!_runActive)
            {
                return;
            }

            _cancelUntilIdle = true;
            _hasPendingRun = false;
            _pendingConfirmedMassDeletes = null;
            cts = _cts;
        }

        cts?.Cancel();
    }

    /// <summary>Cancels the current run, drops queued follow-ups, and waits for cleanup.</summary>
    public async Task CancelAndWaitAsync()
    {
        CancellationTokenSource? cts;
        Task? activeRun;
        lock (_runGate)
        {
            _acceptRunRequests = false;
            _cancelUntilIdle = true;
            _hasPendingRun = false;
            _pendingConfirmedMassDeletes = null;
            cts = _cts;
            activeRun = _runCompletion?.Task;
        }

        DetachTrigger();
        cts?.Cancel();
        if (activeRun is not null)
        {
            await activeRun;
        }
    }

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private Task Delete() => _onDelete(this);

    // Task shape convention (AGENT.md): Move is exclusive, so a task whose destination
    // is Move cannot take another one.
    private bool CanAddDestination() =>
        Children.All(child => child.Destination.Strategy != SyncStrategy.Move);

    /// <summary>Tooltip for the add-destination button, explaining why it is disabled.</summary>
    public string AddDestinationHint => CanAddDestination()
        ? Strings.Task_AddDestinationTip
        : Strings.Common_MoveExclusiveHint;

    [RelayCommand(CanExecute = nameof(CanAddDestination))]
    private async Task AddDestination()
    {
        var destination = await _dialogs.EditDestinationAsync(
            null, Task.SourcePath, hasSiblings: Children.Count > 0, _destinationConflicts);
        if (destination != null)
        {
            Children.Add(NewChild(destination, DestinationSyncStatus.Never(destination.Id)));
            RebuildAndPersist();
        }
    }

    private async Task EditLeaf(DestinationNodeViewModel node)
    {
        var edited = await _dialogs.EditDestinationAsync(
            node.Destination, Task.SourcePath, hasSiblings: Children.Count > 1, _destinationConflicts);
        if (edited != null)
        {
            // Id is preserved by the editor, so the existing status still applies.
            Children[Children.IndexOf(node)] = NewChild(edited, node.Status);
            RebuildAndPersist();
        }
    }

    private async Task DeleteLeaf(DestinationNodeViewModel node)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Strings.Task_DeleteDestinationTitle,
            Localizer.Format(Strings.Task_DeleteDestinationMessageFormat, node.Name),
            Strings.Common_Delete);
        if (!confirmed)
        {
            return;
        }

        Children.Remove(node);
        RebuildAndPersist();
    }

    private DestinationNodeViewModel NewChild(Destination destination, DestinationSyncStatus status) =>
        new(destination, status, EditLeaf, DeleteLeaf, ConfirmLeaf);

    // A destination is blocked on a mass-delete confirmation: preview the current deletions,
    // ask the user in an independent window, and re-run just that destination if they approve.
    private async Task ConfirmLeaf(DestinationNodeViewModel node)
    {
        var preview = await _engine.PreviewMirrorDeletionsAsync(Task, node.Id);
        if (preview.Count == 0)
        {
            // The situation resolved (e.g. the source came back) — just run normally.
            await RunAsync();
            return;
        }

        var approved = await _confirmer.ConfirmAsync(new MirrorDeleteRequest(
            node.Name, node.Path, node.Destination.DeleteMode, preview));

        if (approved)
        {
            await RunAsync(new HashSet<Guid> { node.Id });
        }
    }

    // Destinations are immutable on the task, so rebuild it from the child nodes.
    private void RebuildAndPersist()
    {
        Task = Task with { Destinations = Children.Select(child => child.Destination).ToList() };
        _onChanged();
    }

    private void RefreshHealth()
    {
        OnPropertyChanged(nameof(HealthOutcome));
        OnPropertyChanged(nameof(HealthText));
    }

    // Wires the task's trigger (scheduled/watch) to run the sync automatically. Manual
    // tasks get an inert source that never fires. A failure to start (e.g. a missing
    // watch directory) degrades to manual-only rather than crashing the app.
    private void StartTrigger()
    {
        try
        {
            _triggerSource = _triggerFactory.Create(Task.Trigger, Task.SourcePath);
            _triggerSource.Fired += OnTriggerFired;
            _triggerSource.Error += OnTriggerError;
            _triggerSource.Recovered += OnTriggerRecovered;
            _triggerSource.Start();
            TriggerError = null;
        }
        catch (Exception exception)
        {
            // Degrade to manual-only rather than crashing, but no longer silently — a bad
            // watch path or cron would otherwise never run with no explanation. Surface it
            // both to the log and to the card (an amber badge) so the user knows the task
            // won't run automatically.
            ReportTriggerFailure(exception, mayRecover: false);
        }
    }

    private async void OnTriggerFired(object? sender, EventArgs e) => await RunAsync();

    private void OnTriggerError(Exception exception)
    {
        ReportTriggerFailure(exception, mayRecover: true);
    }

    private void ReportTriggerFailure(Exception exception, bool mayRecover)
    {
        _logger.LogError(exception, "Trigger failed for task '{Task}'.", Task.Name);
        // The inner exception message stays as the engine produced it (English); only the
        // wrapper sentence is localized, and it re-renders on the next trigger event rather
        // than on a language switch (stored, not computed — acceptable staleness).
        _dispatcher.Post(() => TriggerError = Localizer.Format(
            mayRecover ? Strings.Task_TriggerErrorRecoverableFormat : Strings.Task_TriggerErrorStartFormat,
            exception.Message));
    }

    private void OnTriggerRecovered() => _dispatcher.Post(() => TriggerError = null);

    // Single entry point for both manual and triggered runs. Requests received during an
    // active run coalesce into one follow-up; a confirmed request takes priority so a user's
    // approved deletion set can never be overwritten by a plain trigger.
    // <paramref name="confirmedMassDeletes"/> carries destinations whose mass-delete the user
    // just approved, so this run applies their deletions.
    private Task RunAsync(IReadOnlySet<Guid>? confirmedMassDeletes = null)
    {
        TaskCompletionSource completion;
        lock (_runGate)
        {
            if (!_acceptRunRequests || _cancelUntilIdle)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            if (_runActive)
            {
                _hasPendingRun = true;
                if (confirmedMassDeletes is not null)
                {
                    // Union rather than replace: approvals for different destinations can
                    // queue during the same active run, and each one must survive.
                    _pendingConfirmedMassDeletes = _pendingConfirmedMassDeletes is null
                        ? confirmedMassDeletes
                        : _pendingConfirmedMassDeletes.Union(confirmedMassDeletes).ToHashSet();
                }

                return System.Threading.Tasks.Task.CompletedTask;
            }

            _runActive = true;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _runCompletion = completion;
        }

        _ = CompleteDrainAsync(confirmedMassDeletes, completion);
        return completion.Task;
    }

    private async Task CompleteDrainAsync(
        IReadOnlySet<Guid>? confirmedMassDeletes,
        TaskCompletionSource completion)
    {
        try
        {
            await DrainRunsAsync(confirmedMassDeletes);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DrainRunsAsync(IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        // A drain is one user-visible burst; its distinct-copied-files accounting starts fresh.
        _burstCopied.Clear();

        var releasedNormally = false;
        try
        {
            var currentRequest = confirmedMassDeletes;
            while (true)
            {
                await RunOnceAsync(currentRequest);

                lock (_runGate)
                {
                    if (_hasPendingRun && _acceptRunRequests && !_cancelUntilIdle)
                    {
                        currentRequest = _pendingConfirmedMassDeletes;
                        _hasPendingRun = false;
                        _pendingConfirmedMassDeletes = null;
                        continue;
                    }

                    _hasPendingRun = false;
                    _pendingConfirmedMassDeletes = null;
                    _runActive = false;
                    if (_acceptRunRequests)
                    {
                        _cancelUntilIdle = false;
                    }

                    releasedNormally = true;
                    return;
                }
            }
        }
        finally
        {
            if (!releasedNormally)
            {
                lock (_runGate)
                {
                    _runActive = false;
                    _hasPendingRun = false;
                    _pendingConfirmedMassDeletes = null;
                    if (_acceptRunRequests)
                    {
                        _cancelUntilIdle = false;
                    }
                }
            }
        }
    }

    private async Task RunOnceAsync(IReadOnlySet<Guid>? confirmedMassDeletes)
    {
        CancellationTokenSource? cts = null;
        IReadOnlyList<DestinationNodeViewModel> runChildren = [];
        IReadOnlyDictionary<Guid, DestinationSyncStatus> priorStatuses =
            new Dictionary<Guid, DestinationSyncStatus>();
        try
        {
            cts = new CancellationTokenSource();
            lock (_runGate)
            {
                _cts = cts;
                if (!_acceptRunRequests || _cancelUntilIdle)
                {
                    cts.Cancel();
                }
            }

            // ObservableCollection may only be enumerated on the UI thread. Work from the
            // stable list after this point so trigger-thread runs never race add/edit/remove.
            runChildren = await _dispatcher.InvokeAsync(() => Children.ToList());
            priorStatuses = runChildren.ToDictionary(child => child.Id, child => child.Status);

            // Task shape convention (AGENT.md): tasks never share same-kind paths. The
            // editors prevent it; hand-edited config is refused here, before any file is
            // touched. Checked on the dispatcher because the probe reads the live task list.
            var overlap = _runOverlapConflict is null
                ? null
                : await _dispatcher.InvokeAsync(() => _runOverlapConflict(Task));
            if (overlap is not null)
            {
                var refused = runChildren
                    .Select(child => new DestinationSyncStatus(
                        child.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0,
                        Localizer.Format(Strings.Task_OverlapRefusedFormat, overlap)))
                    .ToList();
                _dispatcher.Post(() =>
                {
                    foreach (var status in refused)
                    {
                        ChildById(status.DestinationId)?.SetStatus(status);
                    }

                    RefreshHealth();
                });
                _onStatusesUpdated(refused);
                return;
            }

            _dispatcher.Post(() =>
            {
                IsRunning = true;
                foreach (var child in runChildren)
                {
                    child.MarkRunning();
                }

                RefreshHealth();
            });

            var progress = new DispatchedProgress(_dispatcher, OnProgress);
            var statuses = await _engine.ExecuteAsync(Task, cts.Token, progress, confirmedMassDeletes);

            // Coalesced runs are executed independently but reported as one burst: a
            // successful status shows the distinct files copied so far in this drain, so
            // a trailing no-op run cannot reset the row (and status.json) to "0 files".
            statuses = statuses
                .Select(status => status.Outcome == SyncOutcome.Success
                    ? status with { FilesCopied = AccumulateBurstCopies(status) }
                    : status)
                .ToList();

            _dispatcher.Post(() =>
            {
                foreach (var status in statuses)
                {
                    ChildById(status.DestinationId)?.SetStatus(status);
                }

                RefreshHealth();
            });

            _onStatusesUpdated(statuses);
        }
        catch (OperationCanceledException)
        {
            // Revert to the pre-run status; the user stopped it, so it's not a failure.
            _dispatcher.Post(() =>
            {
                foreach (var child in runChildren)
                {
                    if (priorStatuses.TryGetValue(child.Id, out var prior))
                    {
                        child.SetStatus(prior);
                    }
                }

                RefreshHealth();
            });
        }
        catch (Exception exception)
        {
            // Per-destination failures are already captured as statuses by the engine; this
            // catches an unexpected engine/dispatch failure so it is recorded and visible.
            _logger.LogError(exception, "Sync run failed for task '{Task}'.", Task.Name);
            _dispatcher.Post(() =>
            {
                foreach (var child in runChildren)
                {
                    child.SetStatus(new DestinationSyncStatus(
                        child.Id, SyncOutcome.Failed, DateTimeOffset.UtcNow, 0, exception.Message));
                }

                RefreshHealth();
            });
        }
        finally
        {
            lock (_runGate)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
            }

            _dispatcher.Post(() => IsRunning = false);
        }
    }

    // Turns an engine progress report into the destination row's live "Copying x (i/n)" line.
    private void OnProgress(SyncProgress progress)
    {
        var verb = progress.Operation switch
        {
            CopyOperation => Strings.Progress_Copying,
            MoveOperation => Strings.Progress_Moving,
            CreateDirectoryOperation => Strings.Progress_Creating,
            DeleteOperation or DeleteDirectoryOperation => Strings.Progress_Removing,
            _ => Strings.Progress_Syncing,
        };

        ChildById(progress.Destination.Id)?.SetProgress(Localizer.Format(
            Strings.Progress_LineFormat,
            verb, progress.Operation.RelativePath,
            progress.CompletedOperations + 1, progress.TotalOperations));
    }

    // "N files" must be precise: N counts distinct relative paths, so a file copied twice
    // across a burst (rewritten between runs) still counts once.
    private int AccumulateBurstCopies(DestinationSyncStatus status)
    {
        if (!_burstCopied.TryGetValue(status.DestinationId, out var copied))
        {
            copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _burstCopied[status.DestinationId] = copied;
        }

        copied.UnionWith(status.CopiedRelativePaths);
        return copied.Count;
    }

    private DestinationNodeViewModel? ChildById(Guid id) => Children.FirstOrDefault(c => c.Id == id);

    // Marshals engine progress (reported on a background thread) onto the UI dispatcher.
    private sealed class DispatchedProgress : IProgress<SyncProgress>
    {
        private readonly IUiDispatcher _dispatcher;
        private readonly Action<SyncProgress> _handler;

        public DispatchedProgress(IUiDispatcher dispatcher, Action<SyncProgress> handler)
        {
            _dispatcher = dispatcher;
            _handler = handler;
        }

        public void Report(SyncProgress value) => _dispatcher.Post(() => _handler(value));
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_runGate)
        {
            _acceptRunRequests = false;
            _cancelUntilIdle = true;
            _hasPendingRun = false;
            _pendingConfirmedMassDeletes = null;
            cts = _cts;
        }

        cts?.Cancel();
        DetachTrigger();
    }

    private void DetachTrigger()
    {
        ITriggerSource? triggerSource;
        lock (_runGate)
        {
            triggerSource = _triggerSource;
            _triggerSource = null;
        }

        if (triggerSource is null)
        {
            return;
        }

        triggerSource.Fired -= OnTriggerFired;
        triggerSource.Error -= OnTriggerError;
        triggerSource.Recovered -= OnTriggerRecovered;
        triggerSource.Dispose();
    }
}
