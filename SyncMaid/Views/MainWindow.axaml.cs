using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;
using Material.Icons.Avalonia;

namespace SyncMaid.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Caption-button plumbing: pure window state changes, so it lives in the view, not the
    // view model. The buttons carry ElementRole hints for native hit testing; their Click
    // handlers drive the actual window behaviour.
    private void OnMinimize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateChromeForWindowState();
        }
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateChromeForWindowState();
    }

    // Swaps the maximize/restore glyph and applies the maximized inset. When Windows maximizes
    // an extended-client-area window it pushes the frame a few pixels off every edge; without
    // this padding the caption buttons would be clipped at the top and the drag strip would
    // start off-screen. WindowDecorationMargin reports exactly that inset.
    private void UpdateChromeForWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;

        if (this.FindControl<MaterialIcon>("MaximizeIcon") is { } icon)
        {
            icon.Kind = maximized ? MaterialIconKind.WindowRestore : MaterialIconKind.WindowMaximize;
        }

        if (this.FindControl<Border>("TitleBar") is { } titleBar)
        {
            titleBar.Padding = maximized ? WindowDecorationMargin : default;
        }
    }
}
