using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SyncMaid.Controls;

namespace SyncMaid.UiTests.Views;

/// <summary>
/// Pins the notice-row contract (issue #14): a long text wraps inside the box instead of
/// running out of it. Every hint/warning in the app renders through <see cref="HintBox"/>,
/// so this one test covers current and future notice texts.
/// </summary>
public class HintBoxHeadlessTests
{
    private const double HostWidth = 320;

    private static TextBlock ShownText(string text)
    {
        // Top-aligned so the hint takes its natural height instead of filling the window.
        var hint = new HintBox { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = HostWidth, Height = 400, Content = hint };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return hint.GetVisualDescendants().OfType<TextBlock>().Single();
    }

    [AvaloniaFact]
    public void Long_text_wraps_instead_of_overflowing_the_box()
    {
        var oneLine = ShownText("Short.");
        var wrapped = ShownText(string.Concat(Enumerable.Repeat(
            "Verification reads every synced file back over the network. ", 5)));

        Assert.True(wrapped.Bounds.Width <= HostWidth,
            $"text must stay within the box, but measured {wrapped.Bounds.Width}");
        Assert.True(wrapped.Bounds.Height > oneLine.Bounds.Height * 2,
            $"long text must wrap to multiple lines ({wrapped.Bounds.Height} vs one line {oneLine.Bounds.Height})");
    }
}
