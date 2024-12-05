using System.Threading.Tasks;
using Avalonia.Controls;
using SyncMaid.Models;
using SyncMaid.ViewModels;

namespace SyncMaid.Views;

public partial class DestinationEditorWindow : Window
{
    public DestinationEditorWindow()
    {
        InitializeComponent();
    }

    public static async Task<DestinationModel?> ShowDialog(Window parent, DestinationModel? existingDestination = null)
    {
        var dialog = new DestinationEditorWindow();
        var vm = new DestinationEditorViewModel(existingDestination);
        dialog.DataContext = vm;
        vm.SetHostWindow(dialog);

        var result = await dialog.ShowDialog<DestinationModel?>(parent);
        return result;
    }
}
