using Avalonia.Controls;
using SyncMaid.ViewModels;
using System;

namespace SyncMaid.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;
        vm.SetHostWindow(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetHostWindow(this);
        }
    }
}