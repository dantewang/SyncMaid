namespace SyncMaid.Models;

public class DestinationModel
{
    public DestinationModel(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; set; }
    public string Path { get; set; }
}