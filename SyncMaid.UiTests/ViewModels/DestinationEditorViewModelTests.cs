using System.Collections.Generic;
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

        var group = Assert.Single(vm.Groups);
        group.NewFilterPattern = "2024";
        group.SelectedFilterKind = FilterKind.Path;
        group.AddRuleCommand.Execute(null);

        Assert.True(vm.OKCommand.CanExecute(null));
    }

    [Fact]
    public void AddRule_maps_kind_to_the_concrete_rule_and_clears_the_pattern()
    {
        var group = Assert.Single(New().Groups);

        group.SelectedFilterKind = FilterKind.Path;
        group.NewFilterPattern = "photos/2024";
        group.AddRuleCommand.Execute(null);

        group.SelectedFilterKind = FilterKind.Extension;
        group.NewFilterPattern = "jpg";
        group.AddRuleCommand.Execute(null);

        Assert.Equal(string.Empty, group.NewFilterPattern);
        Assert.Collection(
            group.Rules,
            f => Assert.Equal("photos/2024", Assert.IsType<PathFilter>(f.Rule).Prefix),
            f => Assert.Equal("jpg", Assert.IsType<ExtensionFilter>(f.Rule).Extension));
    }

    [Fact]
    public void AddRule_is_disabled_without_a_pattern()
    {
        var group = Assert.Single(New().Groups);
        Assert.False(group.AddRuleCommand.CanExecute(null));

        group.NewFilterPattern = "jpg";
        Assert.True(group.AddRuleCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveRule_drops_the_rule()
    {
        var group = Assert.Single(New().Groups);
        group.NewFilterPattern = "jpg";
        group.AddRuleCommand.Execute(null);
        var added = group.Rules.Single();

        group.RemoveRuleCommand.Execute(added);

        Assert.Empty(group.Rules);
    }

    [Fact]
    public void Editing_a_filtered_destination_loads_its_rules_into_one_group()
    {
        var existing = new Destination(
            "Backup",
            @"D:\backup",
            [new PathFilter("docs"), new ExtensionFilter("pdf")],
            SyncStrategy.AddOnly);

        var vm = New(existing: existing);

        Assert.False(vm.SyncAll);
        Assert.Equal(SyncStrategy.AddOnly, vm.SelectedStrategy);
        // A legacy flat list is one ANY group — the group-less simple editor.
        var group = Assert.Single(vm.Groups);
        Assert.False(group.MatchAll);
        Assert.False(vm.HasMultipleGroups);
        Assert.Equal(2, group.Rules.Count);
    }

    [Fact]
    public void Editing_a_sync_all_destination_sets_the_toggle_and_no_listed_filters()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror);

        var vm = New(existing: existing);

        Assert.True(vm.SyncAll);
        Assert.Empty(Assert.Single(vm.Groups).Rules);
    }

    // ---- composition (groups, connectives, exclusion) ----

    private static DestinationEditorViewModel NewValid()
    {
        var vm = New();
        vm.Name = "Backup";
        vm.Path = @"D:\backup";
        vm.SyncAll = false;
        return vm;
    }

    private static void AddRule(FilterGroupViewModel group, FilterKind kind, string pattern)
    {
        group.SelectedFilterKind = kind;
        group.NewFilterPattern = pattern;
        group.AddRuleCommand.Execute(null);
    }

    private static IReadOnlyList<FilterRule> Save(DestinationEditorViewModel vm)
    {
        Destination? result = null;
        vm.CloseRequested += d => result = d;
        vm.OKCommand.Execute(null);
        return result!.Filters;
    }

    [Fact]
    public void A_single_any_group_saves_as_the_legacy_flat_list()
    {
        var vm = NewValid();
        AddRule(vm.Groups[0], FilterKind.Path, "docs");
        AddRule(vm.Groups[0], FilterKind.Extension, "jpg");

        var filters = Save(vm);

        // Collapse-on-save: today's shape, no composite wrapper.
        Assert.Collection(
            filters,
            f => Assert.IsType<PathFilter>(f),
            f => Assert.IsType<ExtensionFilter>(f));
    }

    [Fact]
    public void A_single_all_group_saves_as_one_AllOf()
    {
        var vm = NewValid();
        vm.Groups[0].MatchAll = true;
        AddRule(vm.Groups[0], FilterKind.Path, "docs");
        AddRule(vm.Groups[0], FilterKind.Extension, "jpg");

        var allOf = Assert.IsType<AllOfFilter>(Assert.Single(Save(vm)));
        Assert.Equal(2, allOf.Rules.Count);
    }

    [Fact]
    public void Two_groups_matched_all_build_docs_or_photos_and_jpg()
    {
        // (docs OR photos) AND jpg — the guide's first canonical example.
        var vm = NewValid();
        AddRule(vm.Groups[0], FilterKind.Path, "docs");
        AddRule(vm.Groups[0], FilterKind.Path, "photos");
        vm.AddGroupCommand.Execute(null);
        AddRule(vm.Groups[1], FilterKind.Extension, "jpg");
        vm.MatchAllGroups = true;

        var allOf = Assert.IsType<AllOfFilter>(Assert.Single(Save(vm)));
        Assert.Collection(
            allOf.Rules,
            f => Assert.Equal(2, Assert.IsType<AnyOfFilter>(f).Rules.Count),
            f => Assert.IsType<ExtensionFilter>(f));
    }

    [Fact]
    public void Two_groups_matched_any_build_md_or_docs_and_jpg()
    {
        // md OR (docs AND jpg) — the guide's second canonical example.
        var vm = NewValid();
        AddRule(vm.Groups[0], FilterKind.Extension, "md");
        vm.AddGroupCommand.Execute(null);
        vm.Groups[1].MatchAll = true;
        AddRule(vm.Groups[1], FilterKind.Path, "docs");
        AddRule(vm.Groups[1], FilterKind.Extension, "jpg");

        var filters = Save(vm); // top-level ANY → flat OR list
        Assert.Collection(
            filters,
            f => Assert.IsType<ExtensionFilter>(f),
            f => Assert.Equal(2, Assert.IsType<AllOfFilter>(f).Rules.Count));
    }

    [Fact]
    public void Empty_groups_are_skipped_on_save()
    {
        var vm = NewValid();
        AddRule(vm.Groups[0], FilterKind.Extension, "jpg");
        vm.AddGroupCommand.Execute(null); // second group left empty
        vm.MatchAllGroups = true;

        Assert.IsType<ExtensionFilter>(Assert.Single(Save(vm))); // no wrapper for one real group
    }

    [Fact]
    public void Excluding_a_rule_saves_it_as_Not()
    {
        var vm = NewValid();
        vm.Groups[0].MatchAll = true;
        AddRule(vm.Groups[0], FilterKind.Path, "docs");
        AddRule(vm.Groups[0], FilterKind.Extension, "tmp");
        vm.Groups[0].Rules[1].IsExcluded = true; // docs, but not tmp files

        var allOf = Assert.IsType<AllOfFilter>(Assert.Single(Save(vm)));
        var not = Assert.IsType<NotFilter>(allOf.Rules[1]);
        Assert.IsType<ExtensionFilter>(not.Rule);
    }

    [Fact]
    public void A_composed_destination_raises_back_into_the_same_groups_and_saves_identically()
    {
        var expression = new AllOfFilter(
        [
            new AnyOfFilter([new PathFilter("docs"), new PathFilter("photos")]),
            new NotFilter(new ExtensionFilter("tmp")),
        ]);
        var existing = new Destination("D", @"D:\d", [expression], SyncStrategy.Mirror);

        var vm = New(existing: existing);

        // Raised: top ALL, two groups; the Not became the row's exclude toggle.
        Assert.True(vm.MatchAllGroups);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal(2, vm.Groups[0].Rules.Count);
        Assert.True(Assert.Single(vm.Groups[1].Rules).IsExcluded);

        // And lowering reproduces the identical AST (stable round-trip).
        Assert.Equal(expression, Assert.Single(Save(vm)));
    }

    [Fact]
    public void The_preview_renders_the_expression_as_plain_text()
    {
        var vm = NewValid();
        Assert.Contains("No rules yet", vm.FilterPreview);

        AddRule(vm.Groups[0], FilterKind.Path, "docs");
        AddRule(vm.Groups[0], FilterKind.Path, "photos");
        vm.AddGroupCommand.Execute(null);
        AddRule(vm.Groups[1], FilterKind.Extension, "jpg");
        vm.MatchAllGroups = true;

        Assert.Equal("Syncs: (docs/ or photos/) and jpg", vm.FilterPreview);
    }

    [Fact]
    public void Removing_the_last_group_leaves_a_fresh_empty_one()
    {
        var vm = NewValid();
        vm.AddGroupCommand.Execute(null);
        vm.RemoveGroupCommand.Execute(vm.Groups[0]);
        vm.RemoveGroupCommand.Execute(vm.Groups[0]);

        Assert.Empty(Assert.Single(vm.Groups).Rules); // the panel always has an add-rule input
    }

    [Fact]
    public void Verification_and_delete_mode_round_trip_through_the_editor()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            VerifyContents = true,
            DeleteMode = DeleteMode.Permanent,
        };

        var vm = New(existing: existing);
        Assert.True(vm.VerifyContents);
        Assert.Equal(DeleteMode.Permanent, vm.SelectedDeleteMode);

        Destination? result = null;
        vm.CloseRequested += d => result = d;
        vm.OKCommand.Execute(null);

        Assert.True(result!.VerifyContents);
        Assert.Equal(DeleteMode.Permanent, result.DeleteMode);
    }

    [Fact]
    public void New_destination_defaults_to_recycle_bin_and_no_content_verification()
    {
        var vm = New();

        Assert.False(vm.VerifyContents);
        Assert.Equal(DeleteMode.Recycle, vm.SelectedDeleteMode);
        Assert.True(vm.ConfirmLargeDeletions);   // guard on by default
        Assert.Equal(50m, vm.MassDeletePercent); // at 50%
    }

    [Fact]
    public void Mass_delete_threshold_round_trips_as_a_percentage()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            MassDeleteThreshold = 0.75,
        };

        var vm = New(existing: existing);
        Assert.True(vm.ConfirmLargeDeletions);
        Assert.Equal(75m, vm.MassDeletePercent);

        vm.MassDeletePercent = 30m;
        Destination? result = null;
        vm.CloseRequested += d => result = d;
        vm.OKCommand.Execute(null);

        Assert.Equal(0.30, result!.MassDeleteThreshold, 3);
    }

    [Fact]
    public void Out_of_range_persisted_threshold_is_clamped_before_decimal_conversion()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            MassDeleteThreshold = double.MaxValue,
        };

        var vm = New(existing: existing);

        Assert.True(vm.ConfirmLargeDeletions);
        Assert.Equal(100m, vm.MassDeletePercent);
    }

    [Fact]
    public void Turning_the_guard_off_persists_a_zero_threshold()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror);
        var vm = New(existing: existing);

        vm.ConfirmLargeDeletions = false;
        Destination? result = null;
        vm.CloseRequested += d => result = d;
        vm.OKCommand.Execute(null);

        Assert.Equal(0, result!.MassDeleteThreshold);
    }

    [Fact]
    public void A_zero_threshold_loads_as_the_guard_being_off()
    {
        var existing = new Destination("All", @"D:\all", [new AllFilesFilter()], SyncStrategy.Mirror)
        {
            MassDeleteThreshold = 0,
        };

        Assert.False(New(existing: existing).ConfirmLargeDeletions);
    }

    [Fact]
    public void Enter_saves_when_valid_and_is_ignored_otherwise()
    {
        var vm = New();
        Destination? result = null;
        vm.CloseRequested += d => result = d;

        Assert.False(vm.RequestAccept());   // invalid (no name/path) → not handled, stays open
        Assert.Null(result);

        vm.Name = "Backup";
        vm.Path = @"D:\backup";

        Assert.True(vm.RequestAccept());    // valid → handled, saved
        Assert.NotNull(result);
    }

    [Fact]
    public void Missing_destination_folder_shows_a_hint_without_blocking_save()
    {
        var vm = new DestinationEditorViewModel(
            new FakeFolderPickerService(), existing: null,
            directoryExists: path => path == @"D:\exists");
        vm.Name = "D";

        vm.Path = @"D:\typo";
        Assert.True(vm.ShowPathHint);               // flagged…
        Assert.True(vm.OKCommand.CanExecute(null)); // …but saving is still allowed

        vm.Path = @"D:\exists";
        Assert.False(vm.ShowPathHint);
    }

    [Theory]
    [InlineData(@"c:/source/")]
    [InlineData(@"C:\SOURCE\nested")]
    public void Move_destination_at_or_below_source_is_explained_and_rejected(string destinationPath)
    {
        var vm = new DestinationEditorViewModel(
            new FakeFolderPickerService(),
            sourcePath: @"C:\Source",
            directoryExists: _ => true)
        {
            Name = "Unsafe move",
            Path = destinationPath,
            SelectedStrategy = SyncStrategy.Move,
        };

        Assert.True(vm.ShowPathHint);
        Assert.Contains("outside the source", vm.PathHintText, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.OKCommand.CanExecute(null));
    }

    [Fact]
    public void Network_verify_warning_shows_only_for_a_unc_path_with_verification_on()
    {
        var vm = New();
        vm.Path = @"\\nas\backup";
        Assert.True(vm.IsNetworkPath);

        Assert.False(vm.ShowVerifyNetworkWarning); // off until verification is enabled
        vm.VerifyContents = true;
        Assert.True(vm.ShowVerifyNetworkWarning);

        vm.Path = @"D:\local"; // local drive — no warning even with verification on
        Assert.False(vm.IsNetworkPath);
        Assert.False(vm.ShowVerifyNetworkWarning);
    }
}
