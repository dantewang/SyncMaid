namespace SyncMaid.Models;

public class DestinationModel(string name, string path)
{
    public string Name { get; } = name;
    public string Path { get; } = path;

    public DestinationModel WithUpdatedProperties(string? name = null, string? path = null)
    {
        return new DestinationModel(
            name ?? Name,
            path ?? Path
        );
    }
}