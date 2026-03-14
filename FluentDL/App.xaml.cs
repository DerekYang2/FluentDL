using FluentDL.Activation;
using FluentDL.Contracts.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Notifications;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.WindowsAppSDK.Runtime.Packages;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Windows.Graphics.Display;

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
    } = new MainWindow();

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    // Event log
    private const string SourceName = "Application Error";
    private const string LogName = "Application";

    public App()
    {
        try
        {
            InitializeComponent();
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices((context, services) =>
                {
                    // Default Activation Handler
                    services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                    // Other Activation Handlers
                    services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

                    // Services
                    services.AddSingleton<IAppNotificationService, AppNotificationService>();
                    services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
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

                    // Configuration
                    services.Configure<LocalSettingsOptions>(
                        context.Configuration.GetSection(nameof(LocalSettingsOptions)));
                }).Build();

            App.GetService<IAppNotificationService>().Initialize();
        } catch (Exception ex)
        {
            LogException(ex);
            throw;
        }
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Debug.WriteLine($"Non-UI thread exception: {ex}");
            LogException(ex);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Debug.WriteLine($"Non-UI thread exception: {e.Exception}");
            LogException(e.Exception);
        };
    }

    public static void LogException(Exception? ex)
    {
        try
        {
            if (ex == null) return;
            // Create event source if it doesn't exist (requires admin rights)
            if (!EventLog.SourceExists(SourceName))
            {
                EventLog.CreateEventSource(SourceName, LogName);
            }

            string message = $"Exception: {ex.Message}\nStack Trace:\n{ex.StackTrace}";
            EventLog.WriteEntry(SourceName, message, EventLogEntryType.Error);
        }
        catch (Exception logEx)
        {
            // Fallback if event log writing fails
            Debug.WriteLine($"Failed to write to Event Log: {logEx.Message}");
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine(e.Exception.ToString());
        LogException(e.Exception);
        Environment.Exit(1);
    }


    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);
        // App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));

        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
