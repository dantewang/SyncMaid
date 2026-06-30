using SyncMaid.Core.Sync;

namespace SyncMaid.Core.Tests.Sync;

public class MirrorGuardTests
{
    [Fact]
    public void No_deletes_never_trips_the_guard()
    {
        // Even an empty source is fine if nothing would be deleted.
        MirrorGuard.Validate(deleteCount: 0, destinationFileCount: 0, sourceIsEmpty: true, massDeleteThreshold: 0.5);
    }

    [Fact]
    public void Empty_source_with_deletes_is_refused()
    {
        var ex = Assert.Throws<MirrorGuardException>(() =>
            MirrorGuard.Validate(deleteCount: 5, destinationFileCount: 5, sourceIsEmpty: true, massDeleteThreshold: 0.5));

        Assert.Contains("empty or unavailable", ex.Message);
    }

    [Fact]
    public void Mass_delete_over_threshold_on_a_large_destination_is_refused()
    {
        Assert.Throws<MirrorGuardException>(() =>
            MirrorGuard.Validate(deleteCount: 60, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void Deleting_most_of_a_small_destination_is_allowed()
    {
        // Below the ratio-guard floor, deleting "most" of a handful of files is normal.
        MirrorGuard.Validate(deleteCount: 3, destinationFileCount: 3, sourceIsEmpty: false, massDeleteThreshold: 0.5);
    }

    [Fact]
    public void Deletes_under_the_threshold_are_allowed()
    {
        MirrorGuard.Validate(deleteCount: 10, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0.5);
    }

    [Fact]
    public void A_zero_threshold_disables_the_ratio_guard()
    {
        // 0 disables the ratio guard; the empty-source guard still applies elsewhere.
        MirrorGuard.Validate(deleteCount: 100, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0);
    }
}
