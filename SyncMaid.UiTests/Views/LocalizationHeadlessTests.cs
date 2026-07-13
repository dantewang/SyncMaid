using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SyncMaid.Services;

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
}
