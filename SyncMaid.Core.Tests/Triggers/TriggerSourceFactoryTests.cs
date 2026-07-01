using SyncMaid.Core.Tests.IO;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Triggers;

public class TriggerSourceFactoryTests
{
    private static TriggerSourceFactory Factory() => new(new InMemoryFileSystem());

    [Fact]
    public void Create_maps_each_trigger_to_its_source_type()
    {
        var factory = Factory();
        using var manual = factory.Create(new ManualTrigger(), @"C:\src");
        using var scheduled = factory.Create(new ScheduledTrigger("*/5 * * * *"), @"C:\src");
        using var watch = factory.Create(new WatchTrigger(), @"C:\src");

        Assert.IsType<ManualTriggerSource>(manual);
        Assert.IsType<ScheduledTriggerSource>(scheduled);
        Assert.IsType<WatchTriggerSource>(watch);
    }

    [Fact]
    public void Watch_on_a_network_source_uses_polling_instead_of_the_os_watcher()
    {
        var factory = Factory();

        using var local = factory.Create(new WatchTrigger(), @"C:\src");
        using var unc = factory.Create(new WatchTrigger(), @"\\nas\share\src");

        Assert.IsType<WatchTriggerSource>(local);
        Assert.IsType<PollingWatchTriggerSource>(unc);
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
