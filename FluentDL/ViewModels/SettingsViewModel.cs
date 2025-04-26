using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace FluentDL.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    public static readonly string
        FirstRun = "first_run",
        SpotifyClientId = "spotify_client_Id",
        SpotifyClientSecret = "spotify_client_secret",
        DeezerARL = "deezer_arl",
        QobuzId = "qobuz_id",
        QobuzToken = "qobuz_token",
        QobuzEmail = "qobuz_email",
        QobuzPassword = "qobuz_password",
        SearchSource = "search_source",
        DownloadDirectory = "download_directory",
        AskBeforeDownload = "ask_before_download",
        DeezerQuality = "deezer_quality",
        QobuzQuality = "qobuz_quality",
        SpotifyQuality = "spotify_quality",
        YoutubeQuality = "youtube_quality",
        Overwrite = "overwrite",
        CommandThreads = "command_threads",
        ConversionThreads = "conversion_threads",
        AudioConversionThreads = "audio_conversion_threads",
        Notifications = "notifications",
        AutoPlay = "auto_play",
        NotifyUpdate = "notify_update";

    // Shortcut button checkboxes
    public static readonly string SearchAddChecked = "search_add_checked",
        SearchDownloadChecked = "queue_download_checked",
        SearchShareChecked = "search_share_checked",
        SearchOpenChecked = "search_open_checked";

    public static readonly string LocalExplorerAddChecked = "local_explorer_add_checked",
        LocalExplorerEditChecked = "local_explorer_edit_checked",
        LocalExplorerOpenChecked = "local_explorer_open_checked";

    public static readonly string QueueShareChecked = "queue_share_checked",
        QueueDownloadCoverChecked = "queue_download_cover_checked",
        QueueRemoveChecked = "queue_remove_checked";

    public static readonly string FFmpegPath = "ffmpeg_path";

    private static ILocalSettingsService localSettings = App.GetService<ILocalSettingsService>();

    private readonly IThemeSelectorService _themeSelectorService;

    [ObservableProperty] private ElementTheme _elementTheme;

    [ObservableProperty] private string _versionDescription;

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });
    }

    private static async Task SaveSettingsAsyncIfNull<T>(string key, T value)
    {
        if (await localSettings.ReadSettingAsync<T>(key) == null) {
            await localSettings.SaveSettingAsync(key, value);
        }
    }

    public static async Task SetMissingDefaults() // Similar to SetDefaults, but safer by only setting missing settings
    {  
        if (await localSettings.ReadSettingAsync<bool?>(FirstRun) ?? true)
        {  
            Debug.WriteLine("Setting missing defaults");
            await SaveSettingsAsyncIfNull<int?>(CommandThreads, 1);
            await SaveSettingsAsyncIfNull<int?>(ConversionThreads, 3);
            await SaveSettingsAsyncIfNull<int?>(AudioConversionThreads, 6);
            await SaveSettingsAsyncIfNull<int?>(DeezerQuality, 2);
            await SaveSettingsAsyncIfNull<int?>(QobuzQuality, 3);
            await SaveSettingsAsyncIfNull<int?>(SpotifyQuality, 1);
            await SaveSettingsAsyncIfNull<int?>(YoutubeQuality, 1);
            await SaveSettingsAsyncIfNull<bool?>(AskBeforeDownload, true);
            await SaveSettingsAsyncIfNull<bool?>(Overwrite, false);
            await SaveSettingsAsyncIfNull<bool?>(Notifications, false);
            await SaveSettingsAsyncIfNull<bool?>(AutoPlay, true);
            await SaveSettingsAsyncIfNull<bool?>(NotifyUpdate, true);
            await SaveSettingsAsyncIfNull<string?>(DownloadDirectory, null);
            await SaveSettingsAsyncIfNull<string?>(FFmpegPath, null);
            await SaveSettingsAsyncIfNull<string?>(SearchSource, "deezer");
            await SaveSettingsAsyncIfNull<bool?>(SearchAddChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(SearchShareChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(SearchOpenChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(LocalExplorerAddChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(LocalExplorerEditChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(LocalExplorerOpenChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(QueueShareChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(SearchDownloadChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(QueueDownloadCoverChecked, false);
            await SaveSettingsAsyncIfNull<bool?>(QueueRemoveChecked, false);
            await localSettings.SaveSettingAsync(FirstRun, false);  // Set no_settings to 
        }
    }

    public static async Task<T?> GetSetting<T>(string key)
    {
        return await localSettings.ReadSettingAsync<T>(key);
    }


    public static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;

            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}