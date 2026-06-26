using Avalonia;
using Avalonia.Headless;
using SyncMaid.UiTests;

// Registers the headless Avalonia application used by every [AvaloniaFact]/[AvaloniaTheory].
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SyncMaid.UiTests;

/// <summary>
/// Boots the real <see cref="App"/> on Avalonia's headless platform — no display, no
/// GPU — so UI tests run the actual windows, XAML, and bindings in-process and
/// deterministically (the Avalonia analogue of Playwright for web).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::SyncMaid.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
