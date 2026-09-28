using FluentDL.Activation;
using FluentDL.Contracts.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Core.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Notifications;
using FluentDL.Services;
using FluentDL.Services.CustomSpotify;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.WindowsAppSDK.Runtime.Packages;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Windows.Graphics.Display;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Windows.Storage;

namespace FluentDL;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow
    {
        get;
        private set;
    } = null!;

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    private static readonly LoggingLevelSwitch LogLevel = new(LogEventLevel.Information);
    public static string LogDirectory { get; private set; } = string.Empty;
    public static string? LoggingError { get; private set; }
    public static bool IsVerboseLogging => LogLevel.MinimumLevel == LogEventLevel.Debug;

    public App()
    {
        InitializeLogging();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled background exception; terminating: {IsTerminating}", e.IsTerminating);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log.Error(e.Exception, "Unobserved background task failure");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Information("Application process exiting");
            Log.CloseAndFlush();
        };

        try
        {
            Log.Information("Application bootstrap starting; OS {OSVersion}, architecture {Architecture}, packaged {IsPackaged}",
                Environment.OSVersion.Version,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, RuntimeHelper.IsMSIX);
            InitializeComponent();
#if DEBUG
            DebugSettings.LayoutCycleTracingLevel = LayoutCycleTracingLevel.High;
#endif
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSerilog(Log.Logger, dispose: false);
                })
                .ConfigureServices((context, services) =>
                {
                    // Default Activation Handler
                    services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                    // Other Activation Handlers
                    services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

                    // Services
                    services.AddSingleton<IAppNotificationService, AppNotificationService>();
                    services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                    services.AddSingleton<QueueDisplaySettings>();
                    services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                    services.AddTransient<INavigationViewService, NavigationViewService>();

                    services.AddSingleton<IActivationService, ActivationService>();
                    services.AddSingleton<IPageService, PageService>();
                    services.AddSingleton<INavigationService, NavigationService>();

                    // Core Services
                    services.AddSingleton<ISampleDataService, SampleDataService>();
                    services.AddSingleton<IFileService, FileService>();

                    // Views and ViewModels
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<SettingsPage>();
                    services.AddTransient<ContentGridDetailViewModel>();
                    services.AddTransient<ContentGridDetailPage>();
                    services.AddTransient<LocalExplorerViewModel>();
                    services.AddTransient<LocalExplorerPage>();
                    services.AddTransient<QueueViewModel>();
                    services.AddTransient<QueuePage>();
                    services.AddTransient<SplashScreenViewModel>();
                    services.AddTransient<SplashScreenPage>();
                    services.AddTransient<SearchViewModel>();
                    services.AddTransient<Search>();
                    services.AddTransient<ShellPage>();
                    services.AddTransient<ShellViewModel>();

                    // Spotify web player services
                    services.AddHttpClient();
                    services.AddSingleton<IClientTokenService, ClientTokenService>();
                    services.AddHttpClient<ISpotifyISRCService, SpotifyISRCService>();
                    services.AddSingleton<ISpotifyWebService, SpotifyWebService>();
                    services.AddMemoryCache();

                    // Lyrics
                    services.AddTransient<ILyricService, LRCLyricService>();

                    // Configuration
                    services.Configure<LocalSettingsOptions>(
                        context.Configuration.GetSection(nameof(LocalSettingsOptions)));
                }).Build();

            Log.Information("Application services initialized; version {Version}", SettingsViewModel.GetVersionDescription());
            MainWindow = new MainWindow();
            MainWindow.Closed += (_, _) => Log.Information("Main window closing");
            App.GetService<IAppNotificationService>().Initialize();
        } catch (Exception ex)
        {
            Log.Fatal(ex, "Application initialization failed");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static void InitializeLogging()
    {
        Serilog.Debugging.SelfLog.Enable(_ =>
        {
            LoggingError = "Local logging encountered a write error. Check free disk space and folder permissions.";
            Trace.TraceError(LoggingError);
        });

        try
        {
            var localFolder = RuntimeHelper.IsMSIX
                ? ApplicationData.Current.LocalFolder.Path
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentDL");
            LogDirectory = Path.Combine(localFolder, "Logs");
            Log.Logger = DiagnosticLogging.CreateLogger(LogDirectory, LogLevel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LoggingError = "Local logging is unavailable. Check free disk space and folder permissions.";
            Trace.TraceError("{0} ({1})", LoggingError, ex.GetType().Name);
        }
    }

    public static void SetVerboseLogging(bool enabled)
    {
        if (IsVerboseLogging == enabled) return;
        LogLevel.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
#if !DEBUG
        if (Current is App app)
        {
            app.DebugSettings.LayoutCycleTracingLevel = enabled
                ? LayoutCycleTracingLevel.High
                : LayoutCycleTracingLevel.None;
        }
#endif
        Log.Information("Verbose logging {State}", enabled ? "enabled" : "disabled");
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception; application terminating");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }


    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);
        // App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));

        var settings = GetService<ILocalSettingsService>();
        var verboseLogging = await settings.ReadSettingAsync<bool?>(DiagnosticLogging.VerboseSettingKey);
        SetVerboseLogging(verboseLogging ?? false);
        await App.GetService<IActivationService>().ActivateAsync(args);
        if (LoggingError is not null)
        {
            await new ContentDialog
            {
                XamlRoot = MainWindow.Content.XamlRoot,
                Title = "Logging unavailable",
                Content = LoggingError,
                CloseButtonText = "Close"
            }.ShowAsync();
        }
    }
}
