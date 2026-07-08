using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;

namespace SyncMaid.ViewModels;

public partial class DestinationNodeViewModel : ViewModelBase
{
    private readonly Func<DestinationNodeViewModel, Task> _onEdit;
    private readonly Func<DestinationNodeViewModel, Task> _onDelete;
    private readonly Func<DestinationNodeViewModel, Task> _onConfirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Outcome))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    [NotifyPropertyChangedFor(nameof(NeedsConfirmation))]
    private DestinationSyncStatus _status;

    // The live "Copying x (3/120)" line while a run is in progress; null otherwise.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string? _progressText;

    public DestinationNodeViewModel(
        Destination destination,
        DestinationSyncStatus status,
        Func<DestinationNodeViewModel, Task> onEdit,
        Func<DestinationNodeViewModel, Task> onDelete,
        Func<DestinationNodeViewModel, Task> onConfirm)
    {
        Destination = destination;
        _status = status;
        _onEdit = onEdit;
        _onDelete = onDelete;
        _onConfirm = onConfirm;
    }

    /// <summary>The wrapped immutable destination.</summary>
    public Destination Destination { get; }

    public Guid Id => Destination.Id;
    public string Name => Destination.Name;
    public string Path => Destination.LocalPath;

    /// <summary>Current sync outcome — drives the status colour in the view.</summary>
    public SyncOutcome Outcome => Status.Outcome;

    public string StrategyText => Destination.Strategy switch
    {
        SyncStrategy.Mirror => "Mirror",
        SyncStrategy.AddOnly => "Add-only",
        SyncStrategy.Move => "Move",
        _ => Destination.Strategy.ToString(),
    };

    public MaterialIconKind StrategyIconKind => Destination.Strategy switch
    {
        SyncStrategy.Mirror => MaterialIconKind.Sync,
        SyncStrategy.AddOnly => MaterialIconKind.Plus,
        SyncStrategy.Move => MaterialIconKind.ArrowRight,
        _ => MaterialIconKind.Sync,
    };

    public string FilterText => DescribeFilters();

    public string StatusText => Status.Outcome switch
    {
        SyncOutcome.Running => "Syncing…",
        SyncOutcome.Success => $"Synced {Relative(Status.LastRun)} · {Status.FilesCopied} files",
        SyncOutcome.Failed => string.IsNullOrEmpty(Status.Error) ? "Failed" : $"Failed · {Status.Error}",
        SyncOutcome.NeedsConfirmation => "Needs confirmation",
        _ => "Never run",
    };

    /// <summary>What the row shows: the live progress line while running, else the status.</summary>
    public string DisplayStatus => ProgressText ?? StatusText;

    /// <summary>True when a Mirror mass-delete is waiting on the user — drives the Review button.</summary>
    public bool NeedsConfirmation => Outcome == SyncOutcome.NeedsConfirmation;

    /// <summary>Flips to the running state at the start of a sync.</summary>
    public void MarkRunning()
    {
        ProgressText = null;
        Status = Status with { Outcome = SyncOutcome.Running };
    }

    /// <summary>Reports the current operation while the sync runs.</summary>
    public void SetProgress(string text) => ProgressText = text;

    /// <summary>Applies the final status from a completed run and clears any progress line.</summary>
    public void SetStatus(DestinationSyncStatus status)
    {
        ProgressText = null;
        Status = status;
    }

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private Task Delete() => _onDelete(this);

    /// <summary>Opens the independent confirmation window for a blocked mass-delete.</summary>
    [RelayCommand]
    private Task Confirm() => _onConfirm(this);

    private string DescribeFilters()
    {
        var filters = Destination.Filters;
        if (filters is [AllFilesFilter])
        {
            return "All files";
        }

        return filters.Count == 1 ? Describe(filters[0]) : $"{filters.Count} filters";
    }

    private static string Describe(FilterRule rule) => rule switch
    {
        AllFilesFilter => "All files",
        PathFilter path => $"Path: {path.Prefix}",
        ExtensionFilter extension => $"Extension: {extension.Extension}",
        // Composite expression → the compact plain-text form, e.g. "docs/ and (jpg or png)".
        _ => FilterDescriber.Describe(rule),
    };

    private static string Relative(DateTimeOffset? when)
    {
        if (when is null)
        {
            return "—";
        }

        var span = DateTimeOffset.UtcNow - when.Value;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{(int)span.TotalHours} h ago";
        }

        if (span < TimeSpan.FromDays(7))
        {
            return $"{(int)span.TotalDays} d ago";
        }

        return when.Value.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
