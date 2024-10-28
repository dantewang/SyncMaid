using System.Collections.Generic;
using SyncMaid.Core.Filter;
using SyncMaid.Core.Strategy;

namespace SyncMaid.Core.Location;

public class DestinationLocation : ILocation
{
    public List<IFilter> Filters { get; } = [];
    public List<IStrategy> Strategies { get; } = [];
}