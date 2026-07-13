using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using SyncMaid.UiTests;

// Registers the headless Avalonia application used by every [AvaloniaFact]/[AvaloniaTheory].
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// The UI culture is process-global, so language-switching tests cannot overlap tests
// that assert (English) display strings; the suite is small, so run it sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SyncMaid.UiTests;

/// <summary>
/// Boots the real <see cref="App"/> on Avalonia's headless platform — no display, no
/// GPU — so UI tests run the actual windows, XAML, and bindings in-process and
/// deterministically (the Avalonia analogue of Playwright for web).
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Pins the UI culture to English before any test code runs, so display-string
    /// assertions don't depend on the machine's OS language; culture-switching tests restore
    /// this baseline when they finish.</summary>
    [ModuleInitializer]
    public static void PinEnglishUiCulture()
    {
        var english = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::SyncMaid.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
