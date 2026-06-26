using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;

namespace SyncMaid.ViewModels;

public partial class DestinationNodeViewModel : ViewModelBase
{
    private readonly Func<DestinationNodeViewModel, Task> _onEdit;
    private readonly Action<DestinationNodeViewModel> _onDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Outcome))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DestinationSyncStatus _status;

    public DestinationNodeViewModel(
        Destination destination,
        DestinationSyncStatus status,
        Func<DestinationNodeViewModel, Task> onEdit,
        Action<DestinationNodeViewModel> onDelete)
    {
        Destination = destination;
        _status = status;
        _onEdit = onEdit;
        _onDelete = onDelete;
    }

    /// <summary>The wrapped immutable destination.</summary>
    public Destination Destination { get; }

    public Guid Id => Destination.Id;
    public string Name => Destination.Name;
    public string Path => Destination.Path;

    /// <summary>Current sync outcome — drives the status colour in the view.</summary>
    public SyncOutcome Outcome => Status.Outcome;

    public string StrategyText => Destination.Strategy switch
    {
        SyncStrategy.Mirror => "Mirror",
        SyncStrategy.AddOnly => "Add-only",
        SyncStrategy.Move => "Move",
        _ => Destination.Strategy.ToString(),
    };

    public string FilterText => DescribeFilters();

    public string StatusText => Status.Outcome switch
    {
        SyncOutcome.Running => "Syncing…",
        SyncOutcome.Success => $"Synced {Relative(Status.LastRun)} · {Status.FilesCopied} files",
        SyncOutcome.Failed => string.IsNullOrEmpty(Status.Error) ? "Failed" : $"Failed · {Status.Error}",
        _ => "Never run",
    };

    /// <summary>Flips to the running state at the start of a sync.</summary>
    public void MarkRunning() => Status = Status with { Outcome = SyncOutcome.Running };

    /// <summary>Applies the final status from a completed run.</summary>
    public void SetStatus(DestinationSyncStatus status) => Status = status;

    [RelayCommand]
    private Task Edit() => _onEdit(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);

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
        _ => "1 filter",
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
