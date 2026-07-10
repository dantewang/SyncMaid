using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SyncMaid.ViewModels;

namespace SyncMaid.Views;

public partial class MainWindow : Window
{
    // Refreshes the relative "next run in 2 h" badges once a minute (a single view-level timer
    // rather than one per task).
    private readonly DispatcherTimer _scheduleTimer;

    public MainWindow()
    {
        InitializeComponent();

        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _scheduleTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.RefreshSchedules();
        _scheduleTimer.Start();
    }

    // Keyboard for the in-window modal: Esc cancels, Enter performs the dialog's default action.
    // Handled at the window since the overlay isn't focusable; the events bubble up here.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.DialogHost.IsOpen
            && vm.DialogHost.CurrentDialog is IDialogViewModel dialog)
        {
            if (e.Key == Key.Escape)
            {
                dialog.RequestCancel();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && dialog.RequestAccept())
            {
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    // Selecting a task in the sidebar scrolls its card into view, so the sidebar acts as a
    // navigator rather than just expanding the card off-screen.
    private void OnTaskSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedTask: { } task }
            && this.FindControl<ItemsControl>("TasksItemsControl") is { } items
            && items.ContainerFromItem(task) is { } container)
        {
            container.BringIntoView();
        }
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

    // Applies the maximized inset. The glyph swap is declared by a WindowState selector in
    // XAML. When Windows maximizes an extended-client-area window it pushes the frame a few
    // pixels off every edge; without this padding the caption buttons would be clipped at the
    // top and the drag strip would start off-screen. WindowDecorationMargin reports that inset.
    private void UpdateChromeForWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;

        if (this.FindControl<Border>("TitleBar") is { } titleBar)
        {
            titleBar.Padding = maximized ? WindowDecorationMargin : default;
        }
    }
}
