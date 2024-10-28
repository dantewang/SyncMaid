using System.Collections.Generic;
using SyncMaid.Core.Location;

namespace SyncMaid.Core;

public class Sync
{
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

    private List<DestinationLocation> _destinationLocations;
    private SourceLocation _sourceLocation;
}