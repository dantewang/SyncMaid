using System.Collections.ObjectModel;

namespace SyncMaid.Models;

public enum FilterType
{
    Wildcard,
    RegExp
}

public enum SyncStrategy
{
    Sync,
    Addition,
    Move
}

public class FilterRule
{
    public FilterRule(string pattern, FilterType type)
    {
        Pattern = pattern;
        Type = type;
    }

    public string Pattern { get; }
    public FilterType Type { get; }
}

public class DestinationModel
{
    public DestinationModel(
        string name, 
        string path, 
        bool syncAll = true,
        SyncStrategy strategy = SyncStrategy.Sync)
    {
        Name = name;
        Path = path;
        SyncAll = syncAll;
        Strategy = strategy;
        Filters = new ObservableCollection<FilterRule>();
    }

    public DestinationModel(
        string name, 
        string path, 
        bool syncAll,
        SyncStrategy strategy,
        ObservableCollection<FilterRule> filters)
    {
        Name = name;
        Path = path;
        SyncAll = syncAll;
        Strategy = strategy;
        Filters = filters;
    }

    public string Name { get; }
    public string Path { get; }
    public bool SyncAll { get; }
    public SyncStrategy Strategy { get; }
    public ObservableCollection<FilterRule> Filters { get; }

    public DestinationModel WithUpdatedProperties(
        string? name = null, 
        string? path = null,
        bool? syncAll = null,
        SyncStrategy? strategy = null)
    {
        return new DestinationModel(
            name ?? Name,
            path ?? Path,
            syncAll ?? SyncAll,
            strategy ?? Strategy,
            Filters
        );
    }
}