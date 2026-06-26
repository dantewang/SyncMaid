using System.Linq;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class DestinationEditorViewModelTests
{
    private static DestinationEditorViewModel New(string? folder = null, Destination? existing = null) =>
        new(new FakeFolderPickerService(folder), existing);

    [Fact]
    public void Editing_preserves_the_destination_id()
    {
        var existing = new Destination("d", @"D:\d", [new AllFilesFilter()], SyncStrategy.Mirror);
        var vm = New(existing: existing);
        Destination? result = null;
        vm.CloseRequested += d => result = d;

        vm.OKCommand.Execute(null);

        Assert.Equal(existing.Id, result!.Id);
    }

    [Fact]
    public void SyncAll_OK_builds_a_single_all_files_filter()
    {
        var vm = New();
        vm.Name = "Backup";
        vm.Path = @"D:\backup";
        Assert.True(vm.SyncAll);
        Assert.True(vm.OKCommand.CanExecute(null));

        Destination? result = null;
        vm.CloseRequested += d => result = d;
        vm.OKCommand.Execute(null);

        Assert.NotNull(result);
        Assert.IsType<AllFilesFilter>(Assert.Single(result!.Filters));
    }

    [Fact]
    public void When_not_syncing_all_OK_requires_at_least_one_filter()
    {
        var vm = New();
        vm.Name = "Backup";
        vm.Path = @"D:\backup";
        vm.SyncAll = false;

        Assert.False(vm.OKCommand.CanExecute(null));

        vm.NewFilterPattern = "2024";
        vm.SelectedFilterKind = FilterKind.Path;
        vm.AddFilterCommand.Execute(null);

        Assert.True(vm.OKCommand.CanExecute(null));
    }

    [Fact]
    public void AddFilter_maps_kind_to_the_concrete_rule_and_clears_the_pattern()
    {
        var vm = New();

        vm.SelectedFilterKind = FilterKind.Path;
        vm.NewFilterPattern = "photos/2024";
        vm.AddFilterCommand.Execute(null);

        vm.SelectedFilterKind = FilterKind.Extension;
        vm.NewFilterPattern = "jpg";
        vm.AddFilterCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewFilterPattern);
        Assert.Collection(
            vm.Filters,
            f => Assert.Equal("photos/2024", Assert.IsType<PathFilter>(f.Rule).Prefix),
            f => Assert.Equal("jpg", Assert.IsType<ExtensionFilter>(f.Rule).Extension));
    }

    [Fact]
    public void AddFilter_is_disabled_without_a_pattern()
    {
        var vm = New();
        Assert.False(vm.AddFilterCommand.CanExecute(null));

        vm.NewFilterPattern = "jpg";
        Assert.True(vm.AddFilterCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveFilter_drops_the_rule()
    {
        var vm = New();
        vm.NewFilterPattern = "jpg";
        vm.AddFilterCommand.Execute(null);
        var added = vm.Filters.Single();

        vm.RemoveFilterCommand.Execute(added);

        Assert.Empty(vm.Filters);
    }

    [Fact]
    public void Editing_a_filtered_destination_loads_its_rules()
    {
        var existing = new Destination(
            "Backup",
            @"D:\backup",
            [new PathFilter("docs"), new ExtensionFilter("pdf")],
            SyncStrategy.AddOnly);

        var vm = New(existing: existing);

        Assert.False(vm.SyncAll);
        Assert.Equal(SyncStrategy.AddOnly, vm.SelectedStrategy);
        Assert.Equal(2, vm.Filters.Count);
    }

    [Fact]
    public void Editing_a_sync_all_destination_sets_the_toggle_and_no_listed_filters()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror);

        var vm = New(existing: existing);

        Assert.True(vm.SyncAll);
        Assert.Empty(vm.Filters);
    }
}
