using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using SyncMaid.Core.Filtering;
using SyncMaid.Core.Model;
using SyncMaid.Core.Triggers;
using SyncMaid.Services;
using SyncMaid.UiTests.Fakes;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid.UiTests.Views;

/// <summary>
/// Proves the localization mechanics end-to-end on the headless platform: a
/// <c>{l:Loc}</c> compiled binding and a view-model computed string both re-render
/// in place when <see cref="Localizer.Apply"/> switches the UI culture — the
/// no-restart requirement. Tests restore English before finishing because the
/// culture is process-global state.
/// </summary>
public class LocalizationHeadlessTests
{
    [AvaloniaFact]
    public void Loc_binding_and_computed_vm_string_hot_switch_languages()
    {
        var window = new LocProbeWindow { DataContext = new LocProbeViewModel() };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var xamlProbe = window.FindControl<TextBlock>("XamlProbe")!;
            var vmProbe = window.FindControl<TextBlock>("VmProbe")!;
            Assert.Equal("Run all", xamlProbe.Text);
            Assert.Equal("All synced", vmProbe.Text);

            Localizer.Instance.Apply("zh-Hans");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("运行全部", xamlProbe.Text);
            Assert.Equal("全部已同步", vmProbe.Text);

            // zh-TW resolves through the standard fallback chain to zh-Hant.
            Localizer.Instance.Apply("zh-TW");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("執行全部", xamlProbe.Text);

            // Null = system default, which the test bootstrap pins to English.
            Localizer.Instance.Apply(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Run all", xamlProbe.Text);
            Assert.Equal("All synced", vmProbe.Text);
        }
        finally
        {
            Localizer.Instance.Apply("en");
        }
    }

    [AvaloniaFact]
    public void Switching_the_language_in_settings_re_renders_the_main_window_and_persists()
    {
        var dest = new Destination("Backup", @"D:\b", [new AllFilesFilter()], SyncStrategy.Mirror);
        var task = new SyncTask("Photos", @"C:\p", new ManualTrigger(), [dest]);
        var statusStore = new RecordingStatusStore(new Dictionary<Guid, DestinationSyncStatus>
        {
            [dest.Id] = new(dest.Id, SyncOutcome.Success, DateTimeOffset.UtcNow, 5, null),
        });
        var settings = new FakeAppSettingsService();
        var viewModel = new MainWindowViewModel(
            new FakeDialogService(), new RecordingTaskStore([task]), statusStore, new FakeSyncEngine(),
            new FakeTriggerSourceFactory(), new FakeUiDispatcher(), new DialogHost(),
            new FakeAutoStartService(), new FakeMirrorDeleteConfirmer(), settings,
            new FakeConfigLocationService(), new FakeAppRestartService(), NullLoggerFactory.Instance);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            List<string?> Texts() =>
                window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

            Assert.Contains("Run all", Texts());     // toolbar {l:Loc}
            Assert.Contains("All synced", Texts());  // computed VM health string

            // Pick a language exactly as the Settings dialog would.
            var settingsVm = new SettingsViewModel(
                new FakeAutoStartService(), settings,
                new FakeConfigLocationService(), new FakeAppRestartService());
            settingsVm.SelectedLanguage = settingsVm.Languages.Single(option => option.Tag == "zh-Hans");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("zh-Hans", settings.Language); // persisted
            Assert.Contains("运行全部", Texts());        // {l:Loc} re-rendered in place
            Assert.Contains("全部已同步", Texts());      // VM computed string re-rendered
        }
        finally
        {
            Localizer.Instance.Apply("en");
            viewModel.Dispose();
        }
    }
}
