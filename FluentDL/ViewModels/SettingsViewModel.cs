using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    public static readonly string
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
        Notifications = "notifications";

    // Shortcut button checkboxes
    public static readonly string SearchAddChecked = "search_add_checked",
        SearchShareChecked = "search_share_checked",
        SearchOpenChecked = "search_open_checked";

    public static readonly string LocalExplorerAddChecked = "local_explorer_add_checked",
        LocalExplorerEditChecked = "local_explorer_edit_checked",
        LocalExplorerOpenChecked = "local_explorer_open_checked";

    public static readonly string QueueShareChecked = "queue_share_checked",
        QueueDownloadChecked = "queue_download_checked",
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

    public static async Task<T?> GetSetting<T>(string key)
    {
        return await localSettings.ReadSettingAsync<T>(key);
    }


    private static string GetVersionDescription()
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