using System;
using System.Collections.Generic;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;

namespace SyncMaid.UiTests.Fakes;

/// <summary>In-memory <see cref="IStatusStore"/> that records saves so tests can assert persistence.</summary>
public sealed class RecordingStatusStore : IStatusStore
{
    private IReadOnlyDictionary<Guid, DestinationSyncStatus> _data;

    public RecordingStatusStore(IReadOnlyDictionary<Guid, DestinationSyncStatus>? initial = null)
    {
        _data = initial ?? new Dictionary<Guid, DestinationSyncStatus>();
        Saved = _data;
    }

    public int SaveCount { get; private set; }

    public IReadOnlyDictionary<Guid, DestinationSyncStatus> Saved { get; private set; }

    public IReadOnlyDictionary<Guid, DestinationSyncStatus> Load() => _data;

    public void Save(IReadOnlyDictionary<Guid, DestinationSyncStatus> statuses)
    {
        SaveCount++;
        Saved = new Dictionary<Guid, DestinationSyncStatus>(statuses);
        _data = Saved;
    }
}
