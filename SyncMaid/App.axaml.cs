using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private ILogger? _logger;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();

            _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SyncMaid");
            _logger.LogInformation("SyncMaid started.");

            // Last-resort logging for anything that escapes a local handler, so crashes leave
            // a trace in the log file instead of vanishing.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception.");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                _logger.LogError(e.Exception, "Unobserved task exception.");
                e.SetObserved();
            };

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
        var logPath = Path.Combine(configDir, "logs", "syncmaid.log");

        var services = new ServiceCollection();

        // File logging via Microsoft.Extensions.Logging (custom file sink); the log lives
        // next to the config files under the app-data folder.
        services.AddSingleton<ILoggerFactory>(_ =>
            new LoggerFactory(new ILoggerProvider[] { new FileLoggerProvider(logPath) }));

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
        services.AddSingleton<ITriggerSourceFactory>(sp => new TriggerSourceFactory(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher());
        services.AddSingleton<IDialogHost>(_ => new DialogHost());
        services.AddSingleton<IAutoStartService>(_ => new WindowsAutoStartService());
        services.AddSingleton<IMirrorDeleteConfirmer>(_ => new MirrorDeleteConfirmer());
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
            sp.GetRequiredService<IDialogHost>(),
            sp.GetRequiredService<IAutoStartService>(),
            sp.GetRequiredService<IMirrorDeleteConfirmer>(),
            sp.GetRequiredService<ILoggerFactory>()));

        return services.BuildServiceProvider();
    }
}
