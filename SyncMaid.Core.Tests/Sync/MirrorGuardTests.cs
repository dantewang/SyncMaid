using SyncMaid.Core.Sync;

namespace SyncMaid.Core.Tests.Sync;

public class MirrorGuardTests
{
    [Fact]
    public void No_deletes_is_always_allowed()
    {
        // Even an empty source is fine if nothing would be deleted.
        Assert.Equal(
            MirrorGuardVerdict.Allowed,
            MirrorGuard.Evaluate(deleteCount: 0, destinationFileCount: 0, sourceIsEmpty: true, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void Empty_source_with_deletes_is_refused()
    {
        Assert.Equal(
            MirrorGuardVerdict.EmptySource,
            MirrorGuard.Evaluate(deleteCount: 5, destinationFileCount: 5, sourceIsEmpty: true, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void Empty_source_is_not_overridable()
    {
        Assert.Equal(
            MirrorGuardVerdict.EmptySource,
            MirrorGuard.Evaluate(5, 5, sourceIsEmpty: true, massDeleteThreshold: 0.5, overrideMassDelete: true));
    }

    [Fact]
    public void Mass_delete_over_threshold_on_a_large_destination_needs_confirmation()
    {
        Assert.Equal(
            MirrorGuardVerdict.NeedsConfirmation,
            MirrorGuard.Evaluate(deleteCount: 60, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void A_confirmed_mass_delete_is_allowed()
    {
        Assert.Equal(
            MirrorGuardVerdict.Allowed,
            MirrorGuard.Evaluate(60, 100, sourceIsEmpty: false, massDeleteThreshold: 0.5, overrideMassDelete: true));
    }

    [Fact]
    public void Deleting_most_of_a_small_destination_is_allowed()
    {
        // Below the ratio-guard floor, deleting "most" of a handful of files is normal.
        Assert.Equal(
            MirrorGuardVerdict.Allowed,
            MirrorGuard.Evaluate(deleteCount: 3, destinationFileCount: 3, sourceIsEmpty: false, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void Deletes_under_the_threshold_are_allowed()
    {
        Assert.Equal(
            MirrorGuardVerdict.Allowed,
            MirrorGuard.Evaluate(deleteCount: 10, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0.5));
    }

    [Fact]
    public void A_zero_threshold_disables_the_ratio_guard()
    {
        Assert.Equal(
            MirrorGuardVerdict.Allowed,
            MirrorGuard.Evaluate(deleteCount: 100, destinationFileCount: 100, sourceIsEmpty: false, massDeleteThreshold: 0));
    }
}
