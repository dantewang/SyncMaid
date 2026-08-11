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

    /// <summary>Set to simulate a config file that is present but unreadable.</summary>
    public bool Unreadable { get; init; }

    public IReadOnlyList<SyncTask> Load() => Load(out _);

    public IReadOnlyList<SyncTask> Load(out bool unreadable)
    {
        unreadable = Unreadable;
        return Unreadable ? [] : _tasks;
    }

    public void Save(IReadOnlyList<SyncTask> tasks)
    {
        SaveCount++;
        Saved = tasks;
        _tasks = tasks;
    }
}
