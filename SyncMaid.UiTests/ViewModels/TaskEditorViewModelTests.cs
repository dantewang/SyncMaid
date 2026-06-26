using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class TaskEditorViewModelTests
{
    private static TaskEditorViewModel New(string? folder = null, SyncTask? existing = null) =>
        new(new FakeFolderPickerService(folder), existing);

    [Fact]
    public void OK_is_disabled_until_name_and_path_are_set()
    {
        var vm = New();
        Assert.False(vm.OKCommand.CanExecute(null));

        vm.Name = "Photos";
        Assert.False(vm.OKCommand.CanExecute(null));

        vm.Path = @"C:\src";
        Assert.True(vm.OKCommand.CanExecute(null));
    }

    [Fact]
    public void Scheduled_trigger_requires_a_valid_cron_expression()
    {
        var vm = New();
        vm.Name = "Photos";
        vm.Path = @"C:\src";
        vm.SelectedTriggerType = TaskTriggerType.Scheduled;

        Assert.False(vm.OKCommand.CanExecute(null));   // no cron yet

        vm.CronExpression = "not valid";
        Assert.False(vm.OKCommand.CanExecute(null));   // invalid cron

        vm.CronExpression = "*/5 * * * *";
        Assert.True(vm.OKCommand.CanExecute(null));
    }

    [Fact]
    public void IsScheduledTrigger_tracks_the_selected_type()
    {
        var vm = New();
        var changed = false;
        vm.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(vm.IsScheduledTrigger);

        vm.SelectedTriggerType = TaskTriggerType.Scheduled;

        Assert.True(vm.IsScheduledTrigger);
        Assert.True(changed);
    }

    [Theory]
    [InlineData(TaskTriggerType.Manual, typeof(ManualTrigger))]
    [InlineData(TaskTriggerType.Watch, typeof(WatchTrigger))]
    [InlineData(TaskTriggerType.Scheduled, typeof(ScheduledTrigger))]
    public void OK_builds_a_task_with_the_mapped_trigger(TaskTriggerType type, Type expectedTrigger)
    {
        var vm = New();
        vm.Name = "T";
        vm.Path = @"C:\src";
        vm.SelectedTriggerType = type;
        vm.CronExpression = "*/5 * * * *";

        SyncTask? result = null;
        vm.CloseRequested += task => result = task;
        vm.OKCommand.Execute(null);

        Assert.NotNull(result);
        Assert.IsType(expectedTrigger, result!.Trigger);
        Assert.Equal("T", result.Name);
        Assert.Equal(@"C:\src", result.SourcePath);
    }

    [Fact]
    public void Editing_an_existing_task_loads_its_trigger_and_cron()
    {
        var existing = new SyncTask("Old", @"C:\old", new ScheduledTrigger("0 0 * * *"), []);

        var vm = New(existing: existing);

        Assert.Equal("Old", vm.Name);
        Assert.Equal(@"C:\old", vm.Path);
        Assert.Equal(TaskTriggerType.Scheduled, vm.SelectedTriggerType);
        Assert.Equal("0 0 * * *", vm.CronExpression);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = New();
        var raised = false;
        SyncTask? result = null;
        vm.CloseRequested += task => { raised = true; result = task; };

        vm.CancelCommand.Execute(null);

        Assert.True(raised);
        Assert.Null(result);
    }

    [Fact]
    public async Task Browse_sets_path_from_the_folder_picker()
    {
        var vm = New(folder: @"D:\picked");

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\picked", vm.Path);
    }
}
