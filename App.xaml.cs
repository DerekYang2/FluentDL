using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ABI.Microsoft.UI.Xaml.Controls;
using AngleSharp.Dom;
using ATL;
using ATL.Logging;
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

    LoggingTest log = new LoggingTest();

    public static WindowEx MainWindow
    {
        get;
    } = new MainWindow();

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    public static Dictionary<string, MetadataUpdateInfo> metadataUpdates = new Dictionary<string, MetadataUpdateInfo>();

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
                services.AddTransient<DataGridViewModel>();
                services.AddTransient<DataGridPage>();
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

        MainWindow.Closed += async (sender, args) => // Save pending metadata updates on app close, will be applied on restart
        {
            var pendingJsonObject = new JsonObject { ["List"] = new JsonArray() };

            foreach (var metadataUpdateInfo in metadataUpdates.Values)
            {
                pendingJsonObject["List"].AsArray().Add(metadataUpdateInfo.GetJsonObject());
            }

            await App.GetService<ILocalSettingsService>().SaveSettingAsync("PendingMetadata", pendingJsonObject.ToString());
        };
    }

    private async Task UpdateMetadata()
    {
        var jsonStr = await App.GetService<ILocalSettingsService>().ReadSettingAsync<string>("PendingMetadata");
        if (!string.IsNullOrWhiteSpace(jsonStr))
        {
            var rootObject = JsonNode.Parse(jsonStr).AsObject();
            foreach (var item in rootObject["List"].AsArray())
            {
                Debug.WriteLine("SUCCESS: " + MetadataJsonHelper.SaveTrack(item.AsObject()));
            }

            foreach (var logItem in log.messages)
            {
                Debug.WriteLine(logItem.Message);
            }
        }

        // Clear pending metadata updates
        await App.GetService<ILocalSettingsService>().SaveSettingAsync("PendingMetadata", "");
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // TODO: Log and handle exceptions as appropriate.
        Debug.WriteLine(e.Exception + ":" + e.Message);
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        // App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));

        await App.GetService<IActivationService>().ActivateAsync(args);


        // Fetch previous command list
        await LocalCommands.Init();

        // Write pending metadata updates
        Thread t = new Thread(async () =>
            await UpdateMetadata()
        );
        t.Start();

        // Initialize api objects
        var localSettings = App.GetService<ILocalSettingsService>();
        await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));
        await DeezerApi.InitDeezerClient(await localSettings.ReadSettingAsync<string>(SettingsViewModel.DeezerARL));
        Thread t2 = new Thread(async () => // Start separate thread, this takes a while compared to other API wrappers
        {
            QobuzApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken));
            Debug.WriteLine("Logged in Qobuz");
        });
        t2.Start();
    }
}

public class LoggingTest : ILogDevice
{
    public Log theLog = new Log();
    public List<Log.LogItem> messages = new List<Log.LogItem>();

    public LoggingTest()
    {
        LogDelegator.SetLog(ref theLog);
        theLog.Register(this);
    }

    public void DoLog(Log.LogItem anItem)
    {
        messages.Add(anItem);
    }
}