using System.Collections.Generic;
using SyncMaid.Core.Triggers;

namespace SyncMaid.UiTests.Fakes;

/// <summary>Hands out <see cref="FakeTriggerSource"/>s and records every one it created.</summary>
public sealed class FakeTriggerSourceFactory : ITriggerSourceFactory
{
    public List<FakeTriggerSource> Created { get; } = [];

    public ITriggerSource Create(Trigger trigger, string sourcePath)
    {
        var source = new FakeTriggerSource();
        Created.Add(source);
        return source;
    }
}
