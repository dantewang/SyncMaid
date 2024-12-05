#region

using System.Collections.Generic;
using System.Collections.ObjectModel;

#endregion

namespace SyncMaid.Models;

public enum TaskTriggerType
{
    Manual,
    Scheduled,
    Monitoring
}

public class TaskModel
{
    public TaskModel(string name, string path, TaskTriggerType triggerType = TaskTriggerType.Manual, string? cronExpression = null)
    {
        Name = name;
        Path = path;
        TriggerType = triggerType;
        CronExpression = cronExpression;
        Destinations = new ObservableCollection<DestinationModel>();
    }

    public TaskModel(string name, string path, TaskTriggerType triggerType, string? cronExpression, ObservableCollection<DestinationModel> destinations)
    {
        Name = name;
        Path = path;
        TriggerType = triggerType;
        CronExpression = cronExpression;
        Destinations = destinations;
    }

    public string Name { get; }
    public string Path { get; }
    public TaskTriggerType TriggerType { get; }
    public string? CronExpression { get; }
    public ObservableCollection<DestinationModel> Destinations { get; }

    public TaskModel WithUpdatedProperties(
        string? name = null, 
        string? path = null, 
        TaskTriggerType? triggerType = null,
        string? cronExpression = null)
    {
        return new TaskModel(
            name ?? Name,
            path ?? Path,
            triggerType ?? TriggerType,
            cronExpression ?? CronExpression,
            Destinations
        );
    }
}