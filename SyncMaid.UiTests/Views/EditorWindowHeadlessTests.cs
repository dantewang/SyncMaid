using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid.UiTests.Views;

/// <summary>
/// End-to-end UI tests driving the real windows, XAML, and bindings on Avalonia's
/// headless platform — the deterministic replacement for poking the live desktop.
/// </summary>
public class EditorWindowHeadlessTests
{
    [AvaloniaFact]
    public void Typing_into_the_textboxes_flows_through_bindings_to_the_view_model()
    {
        var viewModel = new TaskEditorViewModel(new FakeFolderPickerService());
        var window = new TaskEditorWindow { DataContext = viewModel };
        window.Show();

        var textBoxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
        var nameBox = textBoxes[0];
        var pathBox = textBoxes[1];

        nameBox.Focus();
        window.KeyTextInput("Photos");
        pathBox.Focus();
        window.KeyTextInput(@"C:\src");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Photos", viewModel.Name);
        Assert.Equal(@"C:\src", viewModel.Path);
    }

    [AvaloniaFact]
    public void OK_button_enables_only_when_the_command_can_execute()
    {
        var viewModel = new TaskEditorViewModel(new FakeFolderPickerService());
        var window = new TaskEditorWindow { DataContext = viewModel };
        window.Show();

        var okButton = window.GetVisualDescendants()
            .OfType<Button>()
            .First(button => button.Content as string == "Save task");

        Assert.False(okButton.IsEffectivelyEnabled);

        viewModel.Name = "Photos";
        viewModel.Path = @"C:\src";
        Dispatcher.UIThread.RunJobs();

        Assert.True(okButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void MainWindow_renders_the_tasks_from_the_view_model()
    {
        var store = new RecordingTaskStore([new SyncTask("Photos", @"C:\p", new ManualTrigger(), [])]);
        var viewModel = new MainWindowViewModel(
            new FakeDialogService(), store, new RecordingStatusStore(), new FakeSyncEngine(),
            new FakeTriggerSourceFactory(), new FakeUiDispatcher());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var renderedText = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text);

        Assert.Contains("Photos", renderedText);
    }

    [AvaloniaFact]
    public void MainWindow_card_shows_destination_status_and_strategy()
    {
        var dest = new Destination("Backup", @"D:\b", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("Photos", @"C:\p", new ManualTrigger(), [dest]);
        var statusStore = new RecordingStatusStore(new Dictionary<Guid, DestinationSyncStatus>
        {
            [dest.Id] = new(dest.Id, SyncOutcome.Success, DateTimeOffset.UtcNow, 5, null),
        });
        var viewModel = new MainWindowViewModel(
            new FakeDialogService(), new RecordingTaskStore([task]), statusStore,
            new FakeSyncEngine(), new FakeTriggerSourceFactory(), new FakeUiDispatcher());
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(b => b.Text).ToList();

        Assert.Contains(texts, t => t != null && t.Contains("Synced"));
        Assert.Contains("Mirror", texts);
        Assert.Contains("Manual", texts);   // trigger badge
    }
}
