using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;

namespace SyncMaid.Core.Tests.Model;

public class IdentityTests
{
    private static Destination NewDest() =>
        new("d", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);

    [Fact]
    public void New_records_get_distinct_ids()
    {
        Assert.NotEqual(NewDest().Id, NewDest().Id);
        Assert.NotEqual(
            new SyncTask("t", @"C:\s", new ManualTrigger(), []).Id,
            new SyncTask("t", @"C:\s", new ManualTrigger(), []).Id);
    }

    [Fact]
    public void Id_is_preserved_across_with_edits()
    {
        var dest = NewDest();
        Assert.Equal(dest.Id, (dest with { Name = "renamed" }).Id);

        var task = new SyncTask("t", @"C:\s", new ManualTrigger(), []);
        Assert.Equal(task.Id, (task with { Name = "renamed" }).Id);
    }
}
