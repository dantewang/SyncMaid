#region

using System.Collections.Generic;
using System.Collections.ObjectModel;

#endregion

namespace SyncMaid.Models;

public class TaskModel(string name, string path, ObservableCollection<DestinationModel> destinations)
{
    public TaskModel(string name, string path) : this(name, path, [])
    {
    }

    public string Name { get; } = name;
    public string Path { get; } = path;
    public ObservableCollection<DestinationModel> Destinations { get; } = destinations;

    public TaskModel WithUpdatedProperties(string? name = null, string? path = null)
    {
        return new TaskModel(
            name ?? Name,
            path ?? Path,
            Destinations
        );
    }
}