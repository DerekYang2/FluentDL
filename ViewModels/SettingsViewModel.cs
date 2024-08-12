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
    public static readonly string SpotifyClientId = "spotify_client_Id";
    public static readonly string SpotifyClientSecret = "spotify_client_secret";
    public static readonly string DeezerARL = "deezer_arl";
    public static readonly string QobuzId = "qobuz_id";
    public static readonly string QobuzToken = "qobuz_token";
    public static readonly string SearchSource = "search_source";
    public static readonly string DownloadDirectory = "download_directory";
    public static readonly string AskBeforeDownload = "ask_before_download";
    public static readonly string DeezerQuality = "deezer_quality";
    public static readonly string QobuzQuality = "qobuz_quality";
    public static readonly string SpotifyQuality = "spotify_quality";
    public static readonly string YoutubeQuality = "youtube_quality";
    public static readonly string Overwrite = "overwrite";
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