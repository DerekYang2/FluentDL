using System.Runtime.CompilerServices;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.CustomSpotify;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class SettingsAuthenticationTests
{
    [TestMethod]
    public void SettingsNoticesEscapePublicTextAndKeepOnlyTheIntendedSettingsLink()
    {
        var notice = SettingsNavigation.WithLink("x</a><a href='x'>y", SettingsSection.Tidal);
        var parts = StatusMarkup.Parse(notice);
        Assert.AreEqual(1, parts.Count(part => part.Link != null));
        StringAssert.StartsWith(string.Concat(parts.Select(part => part.Text)), "x</a><a href='x'>y");
        Assert.AreEqual(notice, SettingsNavigation.WithLink(notice, SettingsSection.Tidal));
    }

    [TestMethod]
    [DataRow(SettingsSection.Lyrics, "Settings > Downloads > Lyrics")]
    [DataRow(SettingsSection.Backups, "Settings > General > Backups")]
    [DataRow(SettingsSection.Downloads, "Settings > Downloads > Location")]
    [DataRow(SettingsSection.Updates, "Settings > About > FluentDL > App updates")]
    public void SettingsNoticesUseOneClickablePathWithoutDuplicatingIt(SettingsSection section, string path)
    {
        Assert.AreEqual(path, SettingsNavigation.Path(section));
        var notice = SettingsNavigation.WithLink("Configure this at " + path + ".", section);
        StringAssert.Contains(notice, SettingsNavigation.Link(section));
        Assert.AreEqual(2, notice.Split("fluentdl://", StringSplitOptions.None).Length);
        Assert.AreEqual(notice, SettingsNavigation.WithLink(notice, section));
        var url = new Uri("fluentdl://settings/" + section.ToString().ToLowerInvariant());
        Assert.IsTrue(SettingsNavigation.TryParse(url, out var actual));
        Assert.AreEqual(section, actual);
    }

    [TestMethod]
    public void AuthenticationHeadersUseTheNewGlyphOnlyWhenAvailable()
    {
        Assert.AreEqual("\uEE7E", AuthenticationIcons.SelectProviderGlyph(true));
        Assert.AreEqual("\uEC19", AuthenticationIcons.SelectProviderGlyph(false));
        Assert.IsTrue(AuthenticationIcons.HasGlyph("Segoe MDL2 Assets", '\uEC19'), "The compatibility icon must exist on supported Windows versions.");
        Assert.IsFalse(AuthenticationIcons.HasGlyph("FluentDL nonexistent test font", '\uEE7E'), "Font substitution must not count as glyph support.");
        var fluentHasPasskey = AuthenticationIcons.HasGlyph("Segoe Fluent Icons", '\uEE7E');
        Assert.AreEqual(AuthenticationIcons.SelectProviderGlyph(fluentHasPasskey), AuthenticationIcons.ProviderGlyph);
    }

    [TestMethod]
    public void ServiceSupportHasConsistentStatesAndNoDirectSpotifyDownload()
    {
        var rows = FluentDL.ViewModels.ProviderCapabilityRow.Create(new());
        CollectionAssert.AreEqual(new[] { "Deezer", "Qobuz", "Spotify", "TIDAL" }, rows.Select(row => row.Provider).ToArray());
        Assert.AreEqual("\u2014", rows.Single(row => row.Provider == "Spotify").Downloads.Label);
        Assert.AreEqual("\u2014", rows.Single(row => row.Provider == "Deezer").Search.Label);
        Assert.IsTrue(rows.All(row => SourceCatalog.Find(row.SourceId)?.DisplayName == row.Provider));
        Assert.IsTrue(rows.Where(row => row.Provider != "Deezer").All(row => row.Search.Label == "Unavailable"));
    }

    [TestMethod]
    public void MemberAccountsCanUploadQobuzPlaylistsWithoutFullDownloads()
    {
        var row = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(QobuzSignedIn: true, QobuzCatalog: true)).Single(row => row.Provider == "Qobuz");
        Assert.IsTrue(row.Search.IsReady);
        Assert.IsTrue(row.PlaylistUpload.IsReady);
        Assert.IsTrue(row.PlaylistUrls.IsReady);
        Assert.IsTrue(row.Downloads.IsUnavailable);
        StringAssert.Contains(row.Downloads.Details, "no full-track");
    }

    [TestMethod]
    [DataRow(false, null, FluentDL.ViewModels.CapabilityState.Limited)]
    [DataRow(false, true, FluentDL.ViewModels.CapabilityState.Limited)]
    [DataRow(true, null, FluentDL.ViewModels.CapabilityState.Unknown)]
    [DataRow(true, false, FluentDL.ViewModels.CapabilityState.Limited)]
    [DataRow(true, true, FluentDL.ViewModels.CapabilityState.Ready)]
    public void DeezerPlaylistIndicatorUsesTheSignedInReadCheck(bool signedIn, bool? checkedAccess, FluentDL.ViewModels.CapabilityState expected)
    {
        var row = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(DeezerSignedIn: signedIn,
            DeezerPlaylistReadAccess: checkedAccess)).Single(row => row.Provider == "Deezer");
        Assert.AreEqual(expected, row.PlaylistUrls.State);
        if (expected == FluentDL.ViewModels.CapabilityState.Ready)
        {
            StringAssert.Contains(row.PlaylistUrls.Details, "was checked");
            StringAssert.Contains(row.PlaylistUrls.Details, "Individual playlist permissions");
        }
    }

    [TestMethod]
    public void SignedOutPublicPlaylistAccessIsLimitedAndSearchUsesOneLabel()
    {
        var rows = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(QobuzCatalog: true, SpotifyCatalog: true, TidalCatalog: true));
        foreach (var row in rows.Where(row => row.Provider != "Deezer"))
        {
            Assert.AreEqual("Ready", row.Search.Label);
            Assert.AreEqual(FluentDL.ViewModels.CapabilityState.Limited, row.PlaylistUrls.State);
            Assert.IsTrue(row.PlaylistUpload.IsUnavailable);
        }
    }

    [TestMethod]
    public void FullAndLimitedDownloadAccountsHaveDifferentIndicators()
    {
        var free = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(DeezerSignedIn: true, QobuzSignedIn: true, QobuzFullTracks: true,
            TidalSignedIn: true, TidalSubscription: false));
        Assert.IsTrue(free.Single(row => row.Provider == "Deezer").Downloads.IsWarning);
        Assert.IsTrue(free.Single(row => row.Provider == "Qobuz").Downloads.IsWarning);
        Assert.IsTrue(free.Single(row => row.Provider == "TIDAL").Downloads.IsUnavailable);
        var full = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(DeezerSignedIn: true, DeezerLossless: true,
            QobuzSignedIn: true, QobuzFullTracks: true, QobuzLossless: true, TidalSignedIn: true, TidalSubscription: true));
        Assert.IsTrue(full.Where(row => row.Provider != "Spotify").All(row => row.Downloads.IsReady));
        var unknown = FluentDL.ViewModels.ProviderCapabilityRow.Create(new(TidalSignedIn: true)).Single(row => row.Provider == "TIDAL");
        Assert.IsTrue(unknown.Downloads.IsWarning);
        Assert.AreEqual("Not checked", unknown.Downloads.Label);
    }

    [TestMethod]
    [DataRow(false, true, true, true, false, false)]
    [DataRow(true, false, false, false, false, false)]
    [DataRow(true, null, null, null, false, false)]
    [DataRow(true, true, false, false, true, false)]
    [DataRow(true, false, true, false, true, true)]
    [DataRow(true, false, false, true, true, true)]
    public void QobuzStreamingReadinessUsesGrantedRightsRatherThanMembership(bool signedIn, bool? lossy, bool? lossless, bool? hires, bool full, bool fullLossless)
    {
        var access = QobuzSource.EvaluateStreamingAccess(signedIn, lossy, lossless, hires, null, null, DateTimeOffset.UtcNow);
        Assert.AreEqual(full, access.FullTracks);
        Assert.AreEqual(fullLossless, access.Lossless);
    }

    [TestMethod]
    public void QobuzExpiredOrFutureSubscriptionCannotAppearReady()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsFalse(QobuzSource.EvaluateStreamingAccess(true, true, true, true, null, now.AddDays(-1), now).FullTracks);
        Assert.IsFalse(QobuzSource.EvaluateStreamingAccess(true, true, true, true, now.AddDays(1), null, now).FullTracks);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("spotify-user")]
    public void SpotifyInfoSeparatesCatalogAuthenticationFromTheUserAccount(string? account)
    {
        var text = FluentDL.Views.SettingsPage.FormatSpotifyAccountInfo("Anonymous web player", account);
        StringAssert.Contains(text, "Catalog access:\nAnonymous web player");
        StringAssert.Contains(text, "User account:\n" + (account ?? "Not signed in"));
        Assert.IsFalse(text.Contains("private playlists"));
        Assert.IsFalse(text.Contains("uploads"));
    }

    [TestMethod]
    [DataRow(null, null, "", "", false)]
    [DataRow("", "secret", "", "secret", false)]
    [DataRow("id", "", "id", "", false)]
    [DataRow("id", "secret", "id", "secret", true)]
    [DataRow("id", "secret", " id ", " secret ", true)]
    [DataRow("id", "secret", "changed", "secret", false)]
    [DataRow("id", "secret", "id", "changed", false)]
    [DataRow(null, null, "id", "secret", false)]
    public void SpotifySignInRequiresSavedMatchingCredentials(string? savedId, string? savedSecret, string enteredId, string enteredSecret, bool expected) =>
        Assert.AreEqual(expected, FluentDL.Views.SettingsPage.SpotifyCredentialsMatch(savedId, savedSecret, enteredId, enteredSecret));

    [TestMethod]
    [DataRow(SourceIds.Spotify, SettingsSection.Spotify)]
    [DataRow(SourceIds.Tidal, SettingsSection.Tidal)]
    [DataRow(SourceIds.Deezer, SettingsSection.Deezer)]
    [DataRow(SourceIds.Qobuz, SettingsSection.Qobuz)]
    public void AuthenticationShortcutsHaveTypedDestinationsAndVisiblePaths(string source, SettingsSection expected)
    {
        Assert.AreEqual(expected, SettingsNavigation.ForSource(source));
        StringAssert.StartsWith(SettingsNavigation.Path(expected), "Settings > Authentication > ");
        var uri = new Uri("fluentdl://settings/" + expected.ToString().ToLowerInvariant());
        Assert.IsTrue(SettingsNavigation.TryParse(uri, out var actual));
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(SettingsNavigation.Path(expected), StatusMarkup.Parse(SettingsNavigation.Link(expected)).Single().Text);
    }

    [TestMethod]
    [DataRow("https://settings/spotify")]
    [DataRow("fluentdl://other/spotify")]
    [DataRow("fluentdl://settings/unknown")]
    [DataRow("fluentdl://settings/1")]
    [DataRow("fluentdl://settings/spotify/extra")]
    [DataRow("fluentdl://settings/spotify?run=anything")]
    [DataRow("fluentdl://settings/spotify#anything")]
    [DataRow("fluentdl://user@settings/spotify")]
    public void InternalSettingsLinksRejectUnrecognizedOrParameterizedRoutes(string address) =>
        Assert.IsFalse(SettingsNavigation.TryParse(new Uri(address), out _));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SignedOutTidalDownloadStopsBeforeCreatingDirectoriesOrProcessingRows(bool album)
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var downloads = new DownloadService(new SourceRegistry([]), null!, null!, new TidalDownloadClient(settings));
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "not-created-auth-test-" + Guid.NewGuid());
        SongSearchObject row = album ? new AlbumSearchObject { Source = SourceIds.Tidal, Id = "1" }
            : new SongSearchObject { Source = SourceIds.Tidal, Id = "1" };
        var error = await Assert.ThrowsExceptionAsync<SourceSignInRequiredException>(async () =>
        {
            if (row is AlbumSearchObject selected) await downloads.DownloadAlbum(selected, folder, null!);
            else await downloads.DownloadObject(row, folder, null!);
        });
        Assert.AreEqual(SourceIds.Tidal, error.SourceId);
        Assert.IsFalse(Directory.Exists(folder));
    }

    [TestMethod]
    public async Task NonTidalDownloadsDoNotRequireTheTidalAccount()
    {
        var downloads = new DownloadService(new SourceRegistry([]), null!, null!);
        await downloads.EnsureDownloadAccessAsync([new SongSearchObject { Source = SourceIds.Qobuz }]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PrivateSpotifyImportReportsSettingsSignInRegardlessOfTheLegacyToggle(bool legacyToggle)
    {
        var web = new PlaylistWebService { Private = true };
        var source = new SpotifySource(new TidalParsingTests.DictionarySettings(new() { [SettingKeys.SpotifyPlaylistPrompt] = legacyToggle }), web, null!)
        { IsInitialized = true };
        var rows = new List<SongSearchObject>();
        var error = await Assert.ThrowsExceptionAsync<SourceSignInRequiredException>(() => source.AddFromLinkAsync(rows,
            "https://open.spotify.com/playlist/1234567890123456789012", new(true, new(0, 50), false), null, CancellationToken.None));
        Assert.AreEqual(SourceIds.Spotify, error.SourceId);
        Assert.AreEqual(1, web.PlaylistReads);
        Assert.AreEqual(0, rows.Count);
        Assert.IsFalse(source.IsSignedInForPlaylists);
    }

    [TestMethod]
    public async Task PublicSpotifyImportStillWorksWithoutUserSignIn()
    {
        var web = new PlaylistWebService();
        var source = new SpotifySource(new TidalParsingTests.DictionarySettings([]), web, null!) { IsInitialized = true };
        var rows = new List<SongSearchObject>();
        await source.AddFromLinkAsync(rows, "https://open.spotify.com/playlist/1234567890123456789012", new(true, new(0, 50), false), null, CancellationToken.None);
        Assert.AreEqual(1, rows.Count);
        Assert.IsFalse(source.IsSignedInForPlaylists);
    }

    [TestMethod]
    public async Task CancellingSpotifyImportDoesNotTurnIntoASignInRequest()
    {
        var source = new SpotifySource(new TidalParsingTests.DictionarySettings([]), new PlaylistWebService(), null!) { IsInitialized = true };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => source.AddFromLinkAsync(new List<SongSearchObject>(),
            "https://open.spotify.com/playlist/1234567890123456789012", new(true, new(0, 50), false), null, cancellation.Token));
    }

    private sealed class PlaylistWebService : ISpotifyWebService
    {
        internal bool Private;
        internal int PlaylistReads;
        public async IAsyncEnumerable<SongSearchObject> GetPlaylistAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            PlaylistReads++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Private) throw new HttpRequestException("Playlist is not publicly readable.");
            await Task.Yield();
            yield return new SongSearchObject { Id = "track", Source = SourceIds.Spotify, Title = "Public track" };
        }
        public IAsyncEnumerable<SongSearchObject> SearchAnonymousAsync(string searchTerm, int offset = 0, int limit = 20, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public Task<SongSearchObject?> GetTrack(string id, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public Task<bool> IsAuthenticated(int timeoutMs = 5000, CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }
}
