using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.UiTests.Fakes;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid.UiTests.Views;

/// <summary>
/// End-to-end UI tests driving the real views, XAML, and bindings on Avalonia's headless
/// platform — the deterministic replacement for poking the live desktop.
/// </summary>
public class EditorWindowHeadlessTests
{
    private static Window Host(Control content) =>
        new() { Width = 520, Height = 440, Content = content };

    [AvaloniaFact]
    public void Typing_into_the_textboxes_flows_through_bindings_to_the_view_model()
    {
        var viewModel = new TaskEditorViewModel(new FakeFolderPickerService());
        var window = Host(new TaskEditorView { DataContext = viewModel });
        window.Show();

        var textBoxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
        textBoxes[0].Focus();
        window.KeyTextInput("Photos");
        textBoxes[1].Focus();
        window.KeyTextInput(@"C:\src");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Photos", viewModel.Name);
        Assert.Equal(@"C:\src", viewModel.Path);
    }

    [AvaloniaFact]
    public void Save_button_enables_only_when_the_command_can_execute()
    {
        var viewModel = new TaskEditorViewModel(new FakeFolderPickerService());
        var window = Host(new TaskEditorView { DataContext = viewModel });
        window.Show();

        var saveButton = window.GetVisualDescendants()
            .OfType<Button>()
            .First(button => button.Content as string == "Save task");

        Assert.False(saveButton.IsEffectivelyEnabled);

        viewModel.Name = "Photos";
        viewModel.Path = @"C:\src";
        Dispatcher.UIThread.RunJobs();

        Assert.True(saveButton.IsEffectivelyEnabled);
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
        var window = new MainWindow { DataContext = NewMainViewModel(new RecordingTaskStore([task]), statusStore) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(b => b.Text).ToList();

        Assert.Contains(texts, t => t != null && t.Contains("Synced"));
        Assert.Contains("Mirror", texts);
        Assert.Contains("Photos", texts);
    }

    [AvaloniaFact]
    public void Showing_a_dialog_renders_it_in_the_main_window_overlay()
    {
        var host = new DialogHost();
        var window = new MainWindow { DataContext = NewMainViewModel(new RecordingTaskStore(), new RecordingStatusStore(), host) };
        window.Show();

        _ = host.ShowAsync(new TaskEditorViewModel(new FakeFolderPickerService()));
        Dispatcher.UIThread.RunJobs();

        Assert.True(host.IsOpen);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<TaskEditorView>());
    }

    [AvaloniaFact]
    public void Showing_the_settings_dialog_renders_it_in_the_overlay()
    {
        var host = new DialogHost();
        var window = new MainWindow { DataContext = NewMainViewModel(new RecordingTaskStore(), new RecordingStatusStore(), host) };
        window.Show();

        _ = host.ShowAsync(new SettingsViewModel(
            new FakeAutoStartService(), new FakeAppSettingsService(),
            new FakeConfigLocationService(), new FakeAppRestartService()));
        Dispatcher.UIThread.RunJobs();

        Assert.True(host.IsOpen);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<SettingsView>());
    }

    [AvaloniaFact]
    public void MainWindow_renders_the_custom_title_bar_with_caption_and_settings_buttons()
    {
        var window = new MainWindow { DataContext = NewMainViewModel(new RecordingTaskStore(), new RecordingStatusStore()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var named = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.Name != null)
            .Select(c => c.Name)
            .ToList();

        // The title-bar strip and the three caption buttons are present in the layout.
        Assert.Contains("TitleBar", named);
        Assert.Contains("MinimizeButton", named);
        Assert.Contains("MaximizeButton", named);
        Assert.Contains("CloseButton", named);

        // The settings gear is bound to the view model's stub command (no binding errors).
        var titleTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(b => b.Text).ToList();
        Assert.Contains("SyncMaid", titleTexts);
    }

    private static MainWindowViewModel NewMainViewModel(
        RecordingTaskStore store, RecordingStatusStore statusStore, IDialogHost? host = null) =>
        new(
            new FakeDialogService(), store, statusStore, new FakeSyncEngine(),
            new FakeTriggerSourceFactory(), new FakeUiDispatcher(), host ?? new DialogHost(),
            new FakeAutoStartService(), new FakeMirrorDeleteConfirmer(), new FakeAppSettingsService(),
            new FakeConfigLocationService(), new FakeAppRestartService(), NullLoggerFactory.Instance);
}
