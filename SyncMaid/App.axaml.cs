using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SyncMaid.Core.IO;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Sync;
using SyncMaid.Services;
using SyncMaid.ViewModels;
using SyncMaid.Views;

namespace SyncMaid;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Composition root. Every service is registered with an explicit factory (no
    // reflection-based activation) so the graph stays AOT/trim-safe.
    private static ServiceProvider ConfigureServices()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SyncMaid",
            "tasks.json");

        var services = new ServiceCollection();

        services.AddSingleton<IFileSystem>(_ => new PhysicalFileSystem());
        services.AddSingleton<ITaskStore>(sp => new JsonTaskStore(sp.GetRequiredService<IFileSystem>(), configPath));
        services.AddSingleton(sp => new SyncEngine(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IFolderPickerService>(_ => new AvaloniaFolderPickerService());
        services.AddSingleton<IDialogService>(sp => new DialogService(sp.GetRequiredService<IFolderPickerService>()));
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<ITaskStore>(),
            sp.GetRequiredService<SyncEngine>()));

        return services.BuildServiceProvider();
    }
}
