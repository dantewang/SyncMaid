#region

using System.Collections.Generic;

#endregion

namespace SyncMaid.Models;

public class TaskModel
{
    public TaskModel(string name, string path)
    {
        Name = name;
        Path = path;
        Destinations = [];
    }

    public string Name { get; set; }
    public string Path { get; set; }
    public List<DestinationModel> Destinations { get; set; }
}