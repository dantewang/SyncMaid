using System.Collections.Generic;
using SyncMaid.Core.Model;
using SyncMaid.Core.Persistence;

namespace SyncMaid.UiTests.Fakes;

/// <summary>In-memory <see cref="ITaskStore"/> that records saves so tests can assert persistence.</summary>
public sealed class RecordingTaskStore : ITaskStore
{
    private IReadOnlyList<SyncTask> _tasks;

    public RecordingTaskStore(IReadOnlyList<SyncTask>? initial = null) => _tasks = initial ?? [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<SyncTask> Saved { get; private set; } = [];

    public IReadOnlyList<SyncTask> Load() => _tasks;

    public void Save(IReadOnlyList<SyncTask> tasks)
    {
        SaveCount++;
        Saved = tasks;
        _tasks = tasks;
    }
}
