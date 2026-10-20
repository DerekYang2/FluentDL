using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[DoNotParallelize]
public class TidalCatalogLiveTests
{
    [TestMethod]
    public async Task DownloadAccountSupportsTheCatalogThroughTheRealSource()
    {
        var path = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION for this manual read-only check.");
        var settings = new SessionSettings(path!);
        var downloads = new TidalDownloadClient(settings);
        // No app credentials: a silent fallback cannot satisfy this test.
        var api = new TidalApi(settings, new HttpClient(), downloadClient: downloads) { BuiltInClientId = null, BuiltInClientSecret = null };
        var source = new TidalSource(api);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;

        await api.SignInAsync(token);
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
        Assert.IsTrue(source.IsConnected);
        var scrolled = new List<SongSearchObject>();
        foreach (var page in new[] { new Contracts.Sources.PageRequest(0, 20), new Contracts.Sources.PageRequest(20, 10) })
            await source.SearchAsync(scrolled, "daft punk", page, false, token);
        Assert.AreEqual(30, scrolled.Count);
        Assert.AreEqual(30, scrolled.Select(t => t.Id).Distinct().Count());
        Assert.IsTrue(scrolled.All(t => !string.IsNullOrWhiteSpace(t.Title) && !string.IsNullOrWhiteSpace(t.Artists)));

        var albums = new List<SongSearchObject>();
        await source.SearchAsync(albums, "random access memories", new(0, 3), true, token);
        Assert.IsTrue(albums.OfType<AlbumSearchObject>().Any());
        var advanced = new List<SongSearchObject>();
        await source.AdvancedSearchAsync(advanced, new("Daft Punk", "Get Lucky", ""), new(0, 5), token);
        Assert.IsTrue(advanced.Any(t => t.Isrc == "USQX91300108"));

        var track = await source.GetTrackAsync("20115564", token);
        Assert.IsNotNull(track);
        Assert.AreEqual("USQX91300108", track.Isrc);
        Assert.IsFalse(string.IsNullOrWhiteSpace(track.AlbumName));
        Assert.IsTrue(int.Parse(track.TrackPosition) > 1);
        Assert.IsTrue(track.ImageLocation.StartsWith("https://"));
        var preview = await source.GetPreviewAsync(track);
        Assert.IsTrue(preview.CoverUrl?.StartsWith("https://") == true);
        // An account-only client must not feed full-track DRM into the preview player.
        // TidalSignedInPreviewTests covers playback with separate preview credentials.
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => source.GetPreviewAudioAsync(track));

        var album = await source.GetAlbumAsync("293301129", token);
        Assert.IsNotNull(album);
        var tracks = await source.GetAlbumTracksAsync(album, token);
        Assert.AreEqual(22, tracks.Count, "album pagination must load beyond the first 20 items");
        Assert.AreEqual("1", tracks[0].TrackPosition);
        Assert.IsNotNull(await source.GetTrackByIsrcAsync("USQX91300108", "Random Access Memories", token));
        Assert.IsNotNull(await source.GetAlbumByUpcAsync("886443927087", token));
        var (_, playlist) = await source.GetPlaylistAsync("05182a0b-f4e4-474e-9363-6155171a1519", token);
        Assert.IsTrue(playlist.Count > 20, "playlist pagination must still work");
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
        Console.WriteLine("Download account passed catalog search/paging, advanced search, metadata, covers, preview metadata, album/playlist paging, ISRC and UPC lookup. Preview playback without app credentials was rejected. No playlists changed.");
    }

    internal sealed class SessionSettings(string path) : ILocalSettingsService
    {
        public async Task<T?> ReadSettingAsync<T>(string key) => key == SettingKeys.TidalDownloadSession
            ? (T)(object)Convert.ToBase64String(await File.ReadAllBytesAsync(path)) : default;

        public Task SaveSettingAsync<T>(string key, T value)
        {
            if (key != SettingKeys.TidalDownloadSession) throw new InvalidOperationException("This live test only writes its supplied session file.");
            var text = value as string;
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("The test session expired; sign in again rather than deleting the file.");
            return File.WriteAllBytesAsync(path, Convert.FromBase64String(text));
        }

        public Task<string> ExportSettingsAsync() => throw new NotSupportedException();
        public Task<string?> ImportSettingsAsync(string json) => throw new NotSupportedException();
        public Task ClearSettings() => throw new NotSupportedException();
    }
}
