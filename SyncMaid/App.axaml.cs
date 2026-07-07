using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
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
    private TrayIcon? _trayIcon;

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

            var mainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.MainWindow = mainWindow;

            // Close-to-tray: hiding the main window must not quit the app, so the app owns its
            // own lifetime and exits explicitly (a normal close falls through to Shutdown()
            // in the tray controller when close-to-tray is off).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SetupTray(desktop, mainWindow, services.GetRequiredService<IAppSettingsService>());
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Creates the system-tray icon and routes its menu/click and the window's close through a
    // TrayController. The tray icon itself (Avalonia's TrayIcon/NativeMenu) is created here and
    // is manual-test only; the hide-vs-exit decision lives in the unit-tested TrayController.
    private void SetupTray(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window mainWindow,
        IAppSettingsService settings)
    {
        var controller = new TrayController(settings, new ShellController(mainWindow, desktop));

        var showCommand = new RelayCommand(controller.ShowMainWindow);
        var exitCommand = new RelayCommand(controller.Exit);

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Show main window") { Command = showCommand });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Exit") { Command = exitCommand });

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SyncMaid/Assets/syncmaid.ico"))),
            ToolTipText = "SyncMaid",
            Menu = menu,
            Command = showCommand, // left-click (Win32) opens the window
        };
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

        // Intercept only a genuine user close of the window; an app-shutdown close (e.g. the
        // tray Exit item) must proceed even while close-to-tray is on.
        mainWindow.Closing += (_, e) =>
        {
            if (e.CloseReason == WindowCloseReason.WindowClosing && controller.HandleMainWindowClosing())
            {
                e.Cancel = true;
            }
        };

        desktop.Exit += (_, _) => _trayIcon?.Dispose();
    }

    // Drives the actual Avalonia window/lifetime for the TrayController's decisions.
    private sealed class ShellController : IShellController
    {
        private readonly Window _window;
        private readonly IClassicDesktopStyleApplicationLifetime _desktop;

        public ShellController(Window window, IClassicDesktopStyleApplicationLifetime desktop)
        {
            _window = window;
            _desktop = desktop;
        }

        public void ShowMainWindow()
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Show();
            _window.Activate();
        }

        public void HideMainWindow() => _window.Hide();

        public void Shutdown() => _desktop.Shutdown();
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
        var settingsPath = Path.Combine(configDir, "settings.json");
        var logPath = Path.Combine(configDir, "logs", "syncmaid.log");

        var services = new ServiceCollection();

        // File logging via Microsoft.Extensions.Logging (custom file sink); the log lives
        // next to the config files under the app-data folder.
        services.AddSingleton<ILoggerFactory>(_ =>
            new LoggerFactory(new ILoggerProvider[] { new FileLoggerProvider(logPath) }));

        services.AddSingleton<IFileSystem>(_ => new PhysicalFileSystem());
        services.AddSingleton<ITaskStore>(sp => new JsonTaskStore(sp.GetRequiredService<IFileSystem>(), configPath));
        services.AddSingleton<IStatusStore>(sp => new JsonStatusStore(sp.GetRequiredService<IFileSystem>(), statusPath));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(sp.GetRequiredService<IFileSystem>(), settingsPath));
        services.AddSingleton<IAppSettingsService>(sp => new AppSettingsService(sp.GetRequiredService<ISettingsStore>()));
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
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ILoggerFactory>()));

        return services.BuildServiceProvider();
    }
}
