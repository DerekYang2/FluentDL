using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Services;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// The source adapters the tests call, the same classes the app registers. Their settings come from an in-memory
/// store filled from the secrets file, so ConnectAsync signs in with the test credentials.
/// </summary>
internal static class TestSources
{
    public static readonly DeezerSource Deezer = new();
    public static readonly QobuzSource Qobuz = new();
    public static readonly SpotifySource Spotify = new();
    public static readonly YoutubeSource Youtube = new();

    public static readonly SourceRegistry Registry = new([Deezer, Qobuz, Spotify, Youtube]);

    /// <summary>The queue's conversion path: fills in the ISRC from the origin source, then asks the target.</summary>
    public static readonly ConversionService Conversions = new(Registry);

    /// <summary>The download pipeline the Search and Queue pages use.</summary>
    public static readonly DownloadService Downloads = new(Registry, new EquivalentDownloader(Registry));

    static TestSources()
    {
        var settings = new MemorySettings(new()
        {
            [SettingKeys.DeezerArl] = Secrets.Get("deezer_arl"),
            [SettingKeys.QobuzId] = Secrets.Get("qobuz_id"),
            [SettingKeys.QobuzToken] = Secrets.Get("qobuz_token"),
            [SettingKeys.QobuzAppId] = Secrets.Get("qobuz_app_id"),
            [SettingKeys.QobuzAppSecret] = Secrets.Get("qobuz_app_secret"),
            [SettingKeys.SpotifyClientId] = Secrets.Get("spotify_client_id"),
            [SettingKeys.SpotifyClientSecret] = Secrets.Get("spotify_client_secret"),

            // Download settings, with the values SettingsViewModel.SetMissingDefaults gives a new install.
            [SettingKeys.DeezerQuality] = 2, // FLAC
            [SettingKeys.QobuzQuality] = 3, // 24-bit/192 kHz
            [SettingKeys.SpotifyQuality] = 1, // FLAC
            ["youtube_quality"] = 1,
            [SettingKeys.Overwrite] = false,
            ["use_ytdlp"] = false,
            ["file_naming_wildcard"] = "",
            ["subfolders"] = false,
            ["subfolder_wildcard"] = "",
            ["album_subfolders"] = true,
            ["album_subfolder_wildcard"] = "",
            ["download_album_cover_art"] = true,
            ["download_lyrics"] = false,
            ["replaygain_auto_apply"] = false,
            ["replaygain_album_downloads"] = false,
            ["conversion_threads"] = 3,
        });
        SourceServices.Configure(settings, new NoAudioConverter());
    }

    private sealed class MemorySettings(Dictionary<string, object?> values) : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key) =>
            Task.FromResult(values.TryGetValue(key, out var value) && value is T typed ? typed : default);

        public Task SaveSettingAsync<T>(string key, T value)
        {
            values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string> ExportSettingsAsync() => throw new NotSupportedException();
        public Task<string?> ImportSettingsAsync(string json) => throw new NotSupportedException();
        public Task ClearSettings() => throw new NotSupportedException();
    }

    // The characterization tests don't download, so nothing converts audio.
    private sealed class NoAudioConverter : IAudioConverter
    {
        public bool IsInitialized => false;
        public Task ConvertToFlacAsync(string initialPath, int samplingRate = 48000) => throw new NotSupportedException();
        public Task ConvertToFlacAsync(string inputPath, string outputPath) => throw new NotSupportedException();
        public Task ConvertMp4ToM4aAsync(string inputPath, string outputPath) => throw new NotSupportedException();
        public Task RemuxOpusAsync(string inputPath, string outputPath) => throw new NotSupportedException();
        public Task<string?> CreateMp3Async(string initialPath, string? outputDirectory = null) => throw new NotSupportedException();
    }
}
