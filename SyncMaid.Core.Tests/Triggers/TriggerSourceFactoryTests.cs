using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class TriggerSourceFactoryTests
{
    [Fact]
    public void Create_maps_each_trigger_to_its_source_type()
    {
        using var manual = TriggerSourceFactory.Create(new ManualTrigger(), @"C:\src");
        using var scheduled = TriggerSourceFactory.Create(new ScheduledTrigger("*/5 * * * *"), @"C:\src");
        using var watch = TriggerSourceFactory.Create(new WatchTrigger(), @"C:\src");

        Assert.IsType<ManualTriggerSource>(manual);
        Assert.IsType<ScheduledTriggerSource>(scheduled);
        Assert.IsType<WatchTriggerSource>(watch);
    }

    [Fact]
    public void ManualTriggerSource_Fire_raises_the_event()
    {
        using var source = new ManualTriggerSource();
        var fired = 0;
        source.Fired += (_, _) => fired++;

        source.Fire();
        source.Fire();

        Assert.Equal(2, fired);
    }
}
