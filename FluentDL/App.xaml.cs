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
using Microsoft.UI.Xaml;
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

    public App()
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
                services.AddTransient<SearchViewModel>();
                services.AddTransient<Search>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();

                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            }).Build();
        App.GetService<IAppNotificationService>().Initialize();
        UnhandledException += App_UnhandledException;
        MainWindow.Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        QueueSaver.Close();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // TODO: Log and handle exceptions as appropriate.
        Debug.WriteLine(e.Exception + ":" + e.Message);
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Task.Delay(250);
        base.OnLaunched(args);
        // App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));

        await App.GetService<IActivationService>().ActivateAsync(args);

        if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.FirstRun) ?? true)
            await SettingsViewModel.SetMissingDefaults();

        await QueueViewModel.UpdateShortcutVisibility();
        try
        {
            // Fetch previous command list
            await LocalCommands.Init();

            // Initialize FFMpeg 
            await FFmpegRunner.Initialize();

            // Initialize environment variables
            await KeyReader.Initialize();

            // Initialize api objects
            var localSettings = App.GetService<ILocalSettingsService>();

            // Run seperate thread for synchronous Qobuz initialization
            Thread thread = new Thread(() =>
            {
                var qobuzEmail = DPAPIHelper.Decrypt(localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzEmail).GetAwaiter().GetResult() ?? "");
                var qobuzPassword = DPAPIHelper.Decrypt(localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzPassword).GetAwaiter().GetResult() ?? "");
                var qobuzId = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId).GetAwaiter().GetResult();
                var qobuzToken = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken).GetAwaiter().GetResult();
                var qobuzAppId = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzAppId).GetAwaiter().GetResult();
                var qobuzAppSecret = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzAppSecret).GetAwaiter().GetResult();
                QobuzApi.Initialize(qobuzEmail, qobuzPassword, qobuzId, qobuzToken, qobuzAppId, qobuzAppSecret);
            });
            thread.Priority = ThreadPriority.Highest;
            thread.Start();


            await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));
            await DeezerApi.InitDeezerClient(await localSettings.ReadSettingAsync<string>(SettingsViewModel.DeezerARL));
            await QueueViewModel.LoadSaveQueue();

            QueueSaver.Init();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}