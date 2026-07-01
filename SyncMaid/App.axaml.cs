using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SyncMaid.Core.IO;
using SyncMaid.Core.Persistence;
using SyncMaid.Core.Sync;
using SyncMaid.Core.Triggers;
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
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SyncMaid");
        var configPath = Path.Combine(configDir, "tasks.json");
        var statusPath = Path.Combine(configDir, "status.json");

        var services = new ServiceCollection();

        services.AddSingleton<IFileSystem>(_ => new PhysicalFileSystem());
        services.AddSingleton<ITaskStore>(sp => new JsonTaskStore(sp.GetRequiredService<IFileSystem>(), configPath));
        services.AddSingleton<IStatusStore>(sp => new JsonStatusStore(sp.GetRequiredService<IFileSystem>(), statusPath));
        // Destination provider factory — the extension seam. Local/mounted today; a
        // composite that also routes cloud/SFTP slots in here without touching the engine.
        services.AddSingleton<IDestinationProviderFactory>(sp =>
            new LocalDestinationProviderFactory(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ISyncEngine>(sp => new SyncEngine(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IDestinationProviderFactory>()));
        services.AddSingleton<ITriggerSourceFactory>(_ => new TriggerSourceFactory());
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher());
        services.AddSingleton<IDialogHost>(_ => new DialogHost());
        services.AddSingleton<IFolderPickerService>(_ => new AvaloniaFolderPickerService());
        services.AddSingleton<IDialogService>(sp => new DialogService(
            sp.GetRequiredService<IFolderPickerService>(),
            sp.GetRequiredService<IDialogHost>()));
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<ITaskStore>(),
            sp.GetRequiredService<IStatusStore>(),
            sp.GetRequiredService<ISyncEngine>(),
            sp.GetRequiredService<ITriggerSourceFactory>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IDialogHost>()));

        return services.BuildServiceProvider();
    }
}
