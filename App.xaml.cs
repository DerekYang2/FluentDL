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

    private static JsonObject pendingMetadataJsonObject;

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

        pendingMetadataJsonObject = new JsonObject { ["List"] = new JsonArray() };

        MainWindow.Closed += async (sender, args) => // Save pending metadata updates on app close, will be applied on restart
        {
            var jsonStr = pendingMetadataJsonObject.ToString();
            await App.GetService<ILocalSettingsService>().SaveSettingAsync("PendingMetadata", jsonStr);
        };
    }

    private async Task UpdateMetadata()
    {
        var jsonStr = await App.GetService<ILocalSettingsService>().ReadSettingAsync<string>("PendingMetadata");
        Debug.WriteLine(jsonStr);
        if (!string.IsNullOrWhiteSpace(jsonStr))
        {
            var rootObject = JsonNode.Parse(jsonStr).AsObject();
            foreach (var item in rootObject["List"].AsArray())
            {
                Debug.WriteLine("SUCCESS: " + MetadataJson.SaveTrack(item.AsObject()));
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
    }

    public static void AddMetadataUpdate(MetadataJson metadata)
    {
        var path = metadata.Path;

        // Remove if path already exists
        var arr = pendingMetadataJsonObject["List"].AsArray();
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i]["Path"].GetValue<string>() == path)
            {
                pendingMetadataJsonObject["List"].AsArray().RemoveAt(i);
                break;
            }
        }

        pendingMetadataJsonObject["List"].AsArray().Add(metadata.GetJsonObject());
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

public class MetadataJson
{
    public string Path
    {
        get;
        set;
    }

    public string? ImagePath
    {
        get;
        set;
    } = null;

    public List<MetadataPair> MetadataList
    {
        get;
        set;
    }

    public JsonObject GetJsonObject()
    {
        var rootNode = new JsonObject { ["Path"] = Path, ["MetadataList"] = new JsonArray() };

        rootNode["ImagePath"] = ImagePath ?? "";

        foreach (var pair in MetadataList)
        {
            rootNode["MetadataList"].AsArray().Add(new JsonObject() { ["Key"] = pair.Key, ["Value"] = pair.Value });
        }

        return rootNode;
    }

    public static bool SaveTrack(JsonObject jsonObj)
    {
        var path = jsonObj["Path"].GetValue<string>();
        var imagePath = jsonObj["ImagePath"].GetValue<string>();

        // Check if path still exists 
        if (!File.Exists(path))
        {
            return false;
        }

        var track = new Track(path);

        if (File.Exists(imagePath)) // Save image if it exists
        {
            PictureInfo newPicture = PictureInfo.fromBinaryData(System.IO.File.ReadAllBytes(imagePath), PictureInfo.PIC_TYPE.Front);
            // Append to front if pictures already exist
            if (track.EmbeddedPictures.Count > 0)
            {
                //track.EmbeddedPictures.RemoveAt(0);
                track.EmbeddedPictures.Insert(0, newPicture);
            }
            else
            {
                track.EmbeddedPictures.Add(newPicture);
            }
        }

        foreach (var pair in jsonObj["MetadataList"].AsArray())
        {
            if (pair["Key"].GetValue<string>() == "Title")
            {
                track.Title = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Contributing artists")
            {
                track.Artist = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Genre")
            {
                track.Genre = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Album")
            {
                track.Album = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Album artist")
            {
                track.AlbumArtist = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "ISRC")
            {
                track.ISRC = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "BPM" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var bpm))
                {
                    track.BPM = bpm;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Date" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (DateTime.TryParse(pair["Value"].GetValue<string>(), out var date))
                {
                    track.AdditionalFields["YEAR"] = date.Year.ToString();
                    track.Date = date;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Track number" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var trackNumber))
                {
                    track.TrackNumber = trackNumber;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Track total" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var trackTotal))
                {
                    track.TrackTotal = trackTotal;
                }
            }
            else
            {
                track.AdditionalFields[pair["Key"].GetValue<string>()] = pair["Value"].GetValue<string>();
            }
        }

        return track.Save();
    }
}