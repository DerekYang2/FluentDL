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

    /// <summary>The queue's conversion path: fills in the ISRC from the origin source, then asks the target.</summary>
    public static readonly ConversionService Conversions = new(new SourceRegistry([Deezer, Qobuz, Spotify, Youtube]));

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
        public Task<string?> CreateMp3Async(string initialPath, string? outputDirectory = null) => throw new NotSupportedException();
    }
}
