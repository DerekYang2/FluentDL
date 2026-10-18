using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using Newtonsoft.Json;
using static FluentDL.Characterization.Tests.Ui.QueueWorkflowSteps;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// Check the objects produced by a sequence of UI actions, rather than repeating Search's metadata snapshots.
/// Each workflow previews the converted rows and then the rows restored from the actual guest database.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("Workflow")]
[DoNotParallelize]
public class CatalogWorkflowUiTests
{
    [TestMethod]
    [Timeout(6 * 60 * 1000)]
    public void MixedCatalogQueueConvertsPreviewsAndSurvivesBackupAndRestart()
    {
        RequireSandbox();
        using var settings = new Profile(requireTidal: true);
        SavedRow[] expected = [];
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "mixed-add", () =>
            {
                AddLink(dev, SourceIds.Tidal, $"https://tidal.com/track/{Fixtures.Tracks[0].TidalId}", false);
                AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", true);
                AddLink(dev, SourceIds.Spotify, $"https://open.spotify.com/track/{Fixtures.Tracks[3].SpotifyId}", false);
                expected = WaitSaved(3);
                Assert.AreEqual(1, expected.Count(r => r.Album));
            });
            PreviewEveryRow(dev, expected, "mixed-added");
            Step(dev, "mixed-convert-track-to-spotify-web", () =>
            {
                Convert(dev, ["TIDAL"], "Spotify");
                WaitUntil(() => SavedRows().Count(r => r.Source == SourceIds.Spotify) == 2, DefaultTimeout, "the converted Spotify track to be saved");
                expected = SavedRows();
            });
            PreviewEveryRow(dev, expected, "mixed-spotify-converted");
            Step(dev, "mixed-convert-album-to-tidal", () =>
            {
                Convert(dev, ["Qobuz"], "TIDAL");
                WaitUntil(() => SavedRows().Any(r => r.Source == SourceIds.Tidal && r.Album && r.Tracks == 13),
                    DefaultTimeout, "the converted TIDAL album to be saved");
                expected = SavedRows();
                Assert.AreEqual(3, expected.Length);
            });
            PreviewEveryRow(dev, expected, "mixed-tidal-converted");
            Step(dev, "mixed-clear-and-restore-backup", () =>
            {
                QueueConversionUiTests.ClearQueue(dev); // creates an automatic backup through the app
                RestoreNewestBackup(dev);
                WaitUntil(() => Text(Find(dev, "CountText")) == Views.QueuePage.FormatQueueCount(2, 1), DefaultTimeout, "the mixed backup to restore");
                CollectionAssert.AreEqual(expected, WaitSaved(3), "Backup restore must preserve row identity, kind and order.");
            });
            PreviewEveryRow(dev, expected, "mixed-backup-restored");
        }
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            CollectionAssert.AreEqual(expected, WaitSaved(3));
            SetCompact(dev, true);
            PreviewEveryRow(dev, expected, "mixed-restarted-compact");
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    [TestMethod]
    [TestCategory("SpotifyOfficial")]
    [Timeout(4 * 60 * 1000)]
    public void OfficialSpotifyAlbumPreviewSurvivesConversionAndRestart()
    {
        RequireSandbox();
        // This is separate because Spotify's normal web player mode cannot search albums.
        using var settings = new Profile(officialSpotify: true);
        SavedRow[] expected = [];
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "official-spotify-convert-album", () =>
            {
                AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", true);
                WaitSaved(1);
                Navigate(dev, "Queue");
                Convert(dev, ["Qobuz"], "Spotify");
                WaitUntil(() => SavedRows() is [{ Source: SourceIds.Spotify, Album: true, Tracks: 13 }],
                    DefaultTimeout, "an actual Spotify album from the official API");
                expected = SavedRows();
            });
            PreviewEveryRow(dev, expected, "official-spotify-converted");
        }
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            CollectionAssert.AreEqual(expected, WaitSaved(1));
            SetCompact(dev, true);
            PreviewEveryRow(dev, expected, "official-spotify-restored");
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    [TestMethod]
    [TestCategory("YouTube")]
    [Timeout(3 * 60 * 1000)]
    public void OneYoutubeLinkConvertsToQobuzAndRestoresItsPreview()
    {
        RequireSandbox();
        using var settings = new Profile();
        SavedRow[] expected = [];
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "one-youtube-link", () =>
            {
                // One known link, no search-query variation or album detail fan-out, and no download or playback.
                AddLink(dev, SourceIds.YouTube, $"https://www.youtube.com/watch?v={Fixtures.Tracks[0].YoutubeId}", false);
                WaitSaved(1);
                Navigate(dev, "Queue");
                Convert(dev, ["YouTube"], "Qobuz");
                WaitUntil(() => SavedRows() is [{ Source: SourceIds.Qobuz, Album: false }], DefaultTimeout, "the Qobuz conversion");
                expected = SavedRows();
            });
            PreviewEveryRow(dev, expected, "youtube-to-qobuz");
        }
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            CollectionAssert.AreEqual(expected, WaitSaved(1));
            PreviewEveryRow(dev, expected, "youtube-to-qobuz-restored");
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    internal sealed class Profile : IDisposable
    {
        private readonly Windows.Storage.ApplicationData data;
        private readonly Dictionary<string, object?> previous;

        public Profile(bool officialSpotify = false, bool requireTidal = false, IReadOnlyDictionary<string, object?>? overrides = null)
        {
            var values = new Dictionary<string, object?>
            {
                ["qobuz_id"] = Secrets.Require("qobuz_id"), ["qobuz_token"] = Secrets.Require("qobuz_token"),
                ["qobuz_app_id"] = Secrets.Require("qobuz_app_id"), ["qobuz_app_secret"] = Secrets.Require("qobuz_app_secret"),
                ["tidal_client_id"] = requireTidal ? Secrets.Require("tidal_client_id") : "",
                ["tidal_client_secret"] = requireTidal ? Secrets.Require("tidal_client_secret") : "",
                ["tidal_country"] = "US", ["tidal_refresh_token"] = "", ["tidal_download_session"] = "",
                [SettingKeys.SpotifyClientId] = officialSpotify ? Secrets.Require("spotify_client_id") : "",
                [SettingKeys.SpotifyClientSecret] = officialSpotify ? Secrets.Require("spotify_client_secret") : "",
                [SettingKeys.SpotifyPlaylistPrompt] = false, ["auto_play"] = false,
                ["auto_backup_enabled"] = true, ["queue_compact_layout"] = false,
            };
            foreach (var (key, value) in overrides ?? new Dictionary<string, object?>()) values[key] = value;
            data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(DevApp.FindAumid().Split('!')[0]);
            previous = values.Keys.ToDictionary(key => key, key => data.LocalSettings.Values.TryGetValue(key, out var value) ? value : null);
            foreach (var (key, value) in values) data.LocalSettings.Values[key] = JsonConvert.SerializeObject(value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in previous)
                if (value == null) data.LocalSettings.Values.Remove(key); else data.LocalSettings.Values[key] = value;
        }
    }
}
