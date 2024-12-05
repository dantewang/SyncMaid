using System.Threading.Tasks;
using Avalonia.Controls;
using SyncMaid.Models;
using SyncMaid.ViewModels;

namespace SyncMaid.Views;

public partial class TaskEditorWindow : Window
{
    public TaskEditorWindow()
    {
        InitializeComponent();
    }

    public static async Task<TaskModel?> ShowDialog(Window parent, TaskModel? existingTask = null)
    {
        var dialog = new TaskEditorWindow();
        var vm = new TaskEditorViewModel(existingTask);
        dialog.DataContext = vm;
        vm.SetHostWindow(dialog);

        var result = await dialog.ShowDialog<TaskModel?>(parent);
        return result;
    }
}
