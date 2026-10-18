namespace FluentDL.Core.Sources;

/// <summary>Keys of the settings the sources read. SettingsViewModel's fields of the same names point here.</summary>
public static class SettingKeys
{
    public const string DeezerQuality = "deezer_quality";
    public const string QobuzQuality = "qobuz_quality";
    public const string SpotifyQuality = "spotify_quality";
    public const string Overwrite = "overwrite";

    public const string DeezerArl = "deezer_arl";
    public const string QobuzId = "qobuz_id";
    public const string QobuzToken = "qobuz_token";
    public const string QobuzAppId = "qobuz_app_id";
    public const string QobuzAppSecret = "qobuz_app_secret";
    public const string SpotifyClientId = "spotify_client_Id";
    public const string SpotifyClientSecret = "spotify_client_secret";
    public const string SpotifyUserSession = "spotify_user_session";
    public const string SpotifyPlaylistPrompt = "spotify_playlist_prompt";
    public const string TidalClientId = "tidal_client_id";
    public const string TidalClientSecret = "tidal_client_secret";
    public const string TidalCountry = "tidal_country"; // two-letter code; empty uses the account's country, then the Windows region
    public const string TidalRefreshToken = "tidal_refresh_token"; // the account sign-in, encrypted for the Windows user (DPAPI)
    public const string TidalDownloadSession = "tidal_download_session";
    public const string TidalDownloadQuality = "tidal_download_quality";
    public const string TidalDownloadClientId = "tidal_download_client_id"; // empty uses the Gist, then the cached or bundled ID
    public const string TidalDownloadCachedClient = "tidal_download_cached_client";

    public const string YoutubeQuality = "youtube_quality";
    public const string UseYtdlp = "use_ytdlp";
    public const string YtdlpPath = "ytdlp_path";
}
