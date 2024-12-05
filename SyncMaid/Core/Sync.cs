using System.Collections.Generic;
using SyncMaid.Core.Location;

namespace SyncMaid.Core;

public class Sync
{
    private readonly List<DestinationLocation> _destinationLocations;
    private readonly SourceLocation _sourceLocation;

    public Sync(SourceLocation sourceLocation)
    {
        _sourceLocation = sourceLocation;
        _destinationLocations = new List<DestinationLocation>();
    }

    public void AddDestination(DestinationLocation destination)
    {
        _destinationLocations.Add(destination);
    }

    public void Execute()
    {
        _ensureReadable(_sourceLocation);
        
        foreach (var destinationLocation in _destinationLocations)
        {
            _ensureWritable(destinationLocation);
            
            _executeCopy(_sourceLocation, destinationLocation);
        }
    }

    private void _ensureWritable(DestinationLocation destinationLocation)
    {
        throw new System.NotImplementedException();
    }

    private void _ensureReadable(SourceLocation sourceLocation)
    {
        throw new System.NotImplementedException();
    }

    private void _executeCopy(SourceLocation sourceLocation, DestinationLocation destinationLocation)
    {
        throw new System.NotImplementedException();
    }
}