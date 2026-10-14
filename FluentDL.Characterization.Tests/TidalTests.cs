using System.Collections.ObjectModel;
using System.Text.Json;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// TIDAL through its official API, signed in with the client id and secret from the secrets file. New code, so the
/// baselines are recorded from it, not from the pre-refactor build.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class TidalTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureTidalAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("Tidal.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Tidal.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Tidal.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            Albums = await Live.Collect(items => TestSources.Tidal.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Tidal.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
        };
    });

    // TIDAL pages by cursor, 20 a page; the Search page asks by offset (20 first, then 10 at a time as it scrolls).
    [TestMethod]
    public async Task PagingByOffsetMatchesOneLongRequest()
    {
        var token = CancellationToken.None;
        var scrolled = new List<SongSearchObject>();
        foreach (var page in new PageRequest[] { new(0, 20), new(20, 10), new(30, 10) })
        {
            await TestSources.Tidal.SearchAsync(scrolled, "daft punk", page, albumMode: false, token);
        }
        var atOnce = new List<SongSearchObject>();
        await TestSources.Tidal.SearchAsync(atOnce, "daft punk", new(0, 40), albumMode: false, token);

        Assert.AreEqual(40, scrolled.Count);
        Assert.AreEqual(40, scrolled.Select(s => s.Id).Distinct().Count(), "a page repeated tracks");
        CollectionAssert.AreEqual(atOnce.Select(s => s.Id).ToList(), scrolled.Select(s => s.Id).ToList());
    }

    // Each link form for a track, and an album link in track and album mode. Playlists are checked below without a
    // snapshot, since TIDAL edits them.
    [TestMethod]
    public Task Links() => Snapshot.MatchLive("Tidal.Links", async () =>
    {
        var rows = new List<object>();
        foreach (var (url, albumMode) in new[]
        {
            ("https://tidal.com/track/20115564", false),
            ("https://tidal.com/browse/track/20115564", false),
            ("https://listen.tidal.com/track/20115564", false),
            ("https://tidal.com/album/20115556", false),
            ("https://tidal.com/album/20115556", true),
            ("https://tidal.com/track/1", false), // no such track
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => TestSources.Tidal.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), albumMode), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }
        return rows;
    });

    // Albums and playlists list their tracks 20 a page. This album has 22 tracks and this playlist about 23.
    [TestMethod]
    public async Task LongAlbumsAndPlaylistsLoadEveryPage()
    {
        var album = await TestSources.Tidal.GetAlbumAsync("293301129", CancellationToken.None); // Random Access Memories (10th Anniversary Edition)
        var tracks = await TestSources.Tidal.GetAlbumTracksAsync(album!, CancellationToken.None);
        Assert.AreEqual(22, album!.TracksCount);
        Assert.AreEqual(22, tracks.Select(t => t.Id).Distinct().Count());
        Assert.IsTrue(tracks.All(t => t.AlbumName == album.Title && t.ImageLocation == album.ImageLocation), "album tracks take the album's name and cover");
        Assert.AreEqual("1", tracks[0].TrackPosition);

        var (name, playlistTracks) = await TestSources.Tidal.GetPlaylistAsync("05182a0b-f4e4-474e-9363-6155171a1519", CancellationToken.None);
        Assert.AreEqual("Stromae: Essentials", name);
        Assert.IsTrue(playlistTracks.Count > 20, $"{playlistTracks.Count} tracks: the second page didn't load");
        Assert.AreEqual(playlistTracks.Count, playlistTracks.Select(t => t.Id).Distinct().Count());
    }

    // TIDAL lists Get Lucky's ISRC on 9 releases; the album one is preferred when the album is known.
    [TestMethod]
    public async Task LookupsByIsrcAndUpc()
    {
        var onAlbum = await TestSources.Tidal.GetTrackByIsrcAsync(Fixtures.Tracks[0].Isrc!, "Random Access Memories", CancellationToken.None);
        Assert.AreEqual("20115564", onAlbum?.Id);
        Assert.AreEqual(Fixtures.Tracks[0].Isrc, (await TestSources.Tidal.GetTrackByIsrcAsync(Fixtures.Tracks[0].Isrc!, null, CancellationToken.None))?.Isrc);
        Assert.IsNull(await TestSources.Tidal.GetTrackByIsrcAsync("XX0000000000", null, CancellationToken.None));

        var album = await TestSources.Tidal.GetAlbumByUpcAsync("886443927087", CancellationToken.None);
        Assert.AreEqual("20115556", album?.Id);
        Assert.AreEqual("Random Access Memories", album?.Title);
    }

    // FluentDL's own app, as release builds have it. Local builds get it from %USERPROFILE%\.fluentdl-secrets\build.props.
    [TestMethod]
    public async Task TheBuiltInAccessWorks()
    {
        var api = new TidalApi(new TidalParsingTests.DictionarySettings([]), new HttpClient());
        if (api.BuiltInClientSecret == null)
        {
            Assert.Inconclusive("This build has no FluentDL TIDAL app; see FluentDL.Sources.csproj.");
        }

        await api.SignInAsync();
        var track = await api.GetAsync("tracks/20115564", CancellationToken.None);

        Assert.AreEqual(TidalAccess.BuiltIn, api.Access);
        Assert.AreEqual("USQX91300108", JsonApiDocument.Attribute(track.Data[0], "isrc"));
    }

    // The fields the app shows and matches on, for one known track.
    [TestMethod]
    public async Task ATrackHasItsDetails()
    {
        var results = new List<SongSearchObject>();
        await TestSources.Tidal.SearchAsync(results, "daft punk get lucky random access memories", new(0, 10), albumMode: false, CancellationToken.None);
        var track = results.FirstOrDefault(r => r.Isrc == Fixtures.Tracks[0].Isrc);

        Assert.IsNotNull(track, "Get Lucky (album version) is not in the first 10 results");
        Assert.AreEqual("tidal", track.Source);
        Assert.AreEqual("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", track.Title);
        Assert.AreEqual("Daft Punk, Pharrell Williams, Nile Rodgers", track.Artists);
        Assert.AreEqual("Random Access Memories", track.AlbumName);
        Assert.AreEqual("2013-05-20", track.ReleaseDate);
        Assert.AreEqual("370", track.Duration);
        StringAssert.EndsWith(track.ImageLocation, "/320x320.jpg");
        StringAssert.EndsWith(track.AdditionalFields?["cover_max"] as string, "/1280x1280.jpg");
    }
}

/// <summary>Parsing TIDAL's responses and links. No network.</summary>
[TestClass]
public class TidalParsingTests
{
    [TestMethod]
    [DataRow("https://tidal.com/track/20115564", true)]
    [DataRow("https://tidal.com/browse/track/20115564", true)]
    [DataRow("https://listen.tidal.com/track/20115564", true)]
    [DataRow("https://www.tidal.com/album/20115556", true)]
    [DataRow("https://tidal.com/playlist/05182a0b-f4e4-474e-9363-6155171a1519", true)]
    [DataRow("http://tidal.com/track/1", true)]
    [DataRow("https://tidal.com/artist/1234", false)] // artist links aren't supported yet
    [DataRow("https://nottidal.com/track/1", false)]
    [DataRow("tidal.com/track/1", false)]
    public void LinksTidalHandles(string url, bool handled) => Assert.AreEqual(handled, TestSources.Tidal.CanHandle(url));

    // RFC 7636, appendix B.
    [TestMethod]
    public void TheCodeChallengeIsTheVerifiersHash() =>
        Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", TidalApi.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    // Every request carries the country from the setting; the token request doesn't.
    [TestMethod]
    public async Task RequestsCarryTheCountrySetting()
    {
        var handler = new RecordingHandler();
        var settings = new DictionarySettings(new()
        {
            [Core.Sources.SettingKeys.TidalClientId] = "id",
            [Core.Sources.SettingKeys.TidalClientSecret] = "secret",
            [Core.Sources.SettingKeys.TidalCountry] = "gb",
        });
        var api = new TidalApi(settings, new HttpClient(handler));

        await api.GetAsync("tracks/1?include=albums", CancellationToken.None);

        Assert.AreEqual("https://openapi.tidal.com/v2/tracks/1?include=albums&countryCode=GB", handler.Requests.Last());
        Assert.AreEqual("GB", api.CountryCode);
    }

    // Without a client ID and secret, conversion to TIDAL reports no match (a red badge), not an error.
    [TestMethod]
    public async Task ConversionWithoutCredentialsReportsNotFound()
    {
        var source = new TidalSource(new TidalApi(new DictionarySettings([]), new HttpClient(new RecordingHandler())) { BuiltInClientId = null, BuiltInClientSecret = null });
        var song = new Models.SongSearchObject { Source = "deezer", Id = "67238735", Title = "Get Lucky", Artists = "Daft Punk", AlbumName = "Random Access Memories", Isrc = "USQX91300108" };

        var match = await source.FindEquivalentAsync(song, song.Isrc, CancellationToken.None);

        Assert.AreEqual(Contracts.Sources.MatchKind.NotFound, match.Kind);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var body = request.RequestUri!.Host == "auth.tidal.com"
                ? """{ "access_token": "token", "expires_in": 14400, "token_type": "Bearer" }"""
                : """{ "data": { "id": "1", "type": "tracks", "attributes": {} } }""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    internal sealed class DictionarySettings(Dictionary<string, object?> values) : Core.Contracts.Services.ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key) => Task.FromResult(values.TryGetValue(key, out var value) && value is T typed ? typed : default);
        public Task SaveSettingAsync<T>(string key, T value) { values[key] = value; return Task.CompletedTask; }
        public Task<string> ExportSettingsAsync() => throw new NotSupportedException();
        public Task<string?> ImportSettingsAsync(string json) => throw new NotSupportedException();
        public Task ClearSettings() => throw new NotSupportedException();
    }

    [TestMethod]
    [DataRow("PT6M10S", "370")]
    [DataRow("PT4M8S", "248")]
    [DataRow("PT1H2M3S", "3723")]
    [DataRow("PT45.6S", "46")]
    [DataRow("", "0")]
    [DataRow("not a duration", "0")]
    public void DurationsBecomeSeconds(string iso, string seconds) => Assert.AreEqual(seconds, TidalSource.Seconds(iso));

    [TestMethod]
    [DataRow("GB", "CA", "GB")] // the setting wins
    [DataRow(" de ", "CA", "DE")]
    [DataRow("", "CA", "CA")] // empty: the Windows region
    [DataRow(null, "CA", "CA")]
    [DataRow("Germany", "CA", "CA")] // not a two-letter code
    [DataRow("", "001", "US")] // Windows' "World" region
    public void TheCountryComesFromTheSettingThenWindows(string? setting, string windowsRegion, string code) =>
        Assert.AreEqual(code, TidalApi.ResolveCountry(setting, windowsRegion));

    // A relationship lists references in TIDAL's order (the ranking, or the credit order); "included" can be in any
    // order and can miss some.
    [TestMethod]
    public void RelatedResourcesFollowTheRelationshipOrder()
    {
        var json = """
        {
          "data": { "id": "t1", "type": "tracks", "attributes": { "title": "Song", "version": "Live" },
                    "relationships": { "artists": { "data": [ { "id": "a2", "type": "artists" }, { "id": "a1", "type": "artists" }, { "id": "a9", "type": "artists" } ] } } },
          "included": [
            { "id": "a1", "type": "artists", "attributes": { "name": "First" } },
            { "id": "a2", "type": "artists", "attributes": { "name": "Second" } }
          ]
        }
        """;
        var document = new JsonApiDocument(JsonDocument.Parse(json).RootElement);
        var track = document.Data.Single();

        var names = document.Related(track, "artists").Select(a => JsonApiDocument.Attribute(a, "name")).ToList();

        CollectionAssert.AreEqual(new[] { "Second", "First" }, names); // a9 isn't included, so it's skipped
        Assert.AreEqual("Song (Live)", TidalSource.ConvertTrack(document, track).Title);
        Assert.AreEqual("Second, First", TidalSource.ConvertTrack(document, track).Artists);
    }

    [TestMethod]
    [DataRow("Song", "Live", "Song (Live)")]
    [DataRow("Random Access Memories (10th Anniversary Edition)", "10th Anniversary Edition", "Random Access Memories (10th Anniversary Edition)")]
    [DataRow("Song", "", "Song")]
    public void AVersionIsAddedOnce(string title, string version, string expected)
    {
        var json = $$"""{ "data": { "id": "x", "type": "albums", "attributes": { "title": {{JsonSerializer.Serialize(title)}}, "version": {{JsonSerializer.Serialize(version)}} } } }""";
        var document = new JsonApiDocument(JsonDocument.Parse(json).RootElement);

        Assert.AreEqual(expected, TidalSource.ConvertAlbum(document, document.Data.Single()).Title);
    }
}

/// <summary>
/// Signing in to a TIDAL account, against a fake TIDAL: a fake browser follows the sign-in address and comes back to
/// the real local listener, as TIDAL's sign-in page would. No network beyond 127.0.0.1.
/// </summary>
[TestClass]
public class TidalLoginTests
{
    private static int nextPort = 55440; // not 5544, so a running FluentDL doesn't get in the way

    [TestMethod]
    public async Task SigningInSavesAnEncryptedSignInThatWorksAfterARestart()
    {
        var tidal = new FakeTidal();
        var settings = new TidalParsingTests.DictionarySettings([]);
        var api = NewApi(settings, tidal, out var browser);

        await api.LoginAsync(CancellationToken.None);

        // The code verifier sent with the code hashes to the challenge in the sign-in address, the redirect
        // address matches, and no secret is sent.
        var authorize = Query(browser.Opened!);
        var exchange = tidal.TokenRequests.Single();
        Assert.AreEqual("authorization_code", exchange["grant_type"]);
        Assert.AreEqual("the-code", exchange["code"]);
        Assert.AreEqual(authorize["code_challenge"], TidalApi.CodeChallenge(exchange["code_verifier"]));
        Assert.AreEqual(authorize["redirect_uri"], exchange["redirect_uri"]);
        Assert.AreEqual("test-client", authorize["client_id"]);
        Assert.AreEqual("user.read", authorize["scope"]);
        Assert.IsFalse(exchange.ContainsKey("client_secret"));
        StringAssert.Contains(await browser.Page!, "signed in to TIDAL");

        Assert.AreEqual("derek", api.AccountName);
        Assert.AreEqual("catalog@example.test", api.AccountEmail);
        Assert.AreEqual("catalog@example.test", new TidalSource(api).AccountName);
        Assert.AreEqual("GB", api.CountryCode, "the account's country, with no country setting");
        var saved = (string?)await settings.ReadSettingAsync<string>(Core.Sources.SettingKeys.TidalRefreshToken);
        Assert.IsFalse(string.IsNullOrEmpty(saved));
        Assert.IsFalse(saved!.Contains("refresh-1"), "the refresh token is saved encrypted");

        // A restart: a new TidalApi signs in with the saved token, without the browser.
        var restarted = NewApi(settings, tidal, out _);
        await restarted.SignInAsync();
        Assert.AreEqual("refresh_token", tidal.TokenRequests.Last()["grant_type"]);
        Assert.AreEqual("refresh-1", tidal.TokenRequests.Last()["refresh_token"]);
        Assert.AreEqual("derek", restarted.AccountName);

        await restarted.LogoutAsync();
        Assert.IsNull(restarted.AccountName);
        Assert.IsNull(restarted.AccountEmail);
        Assert.IsFalse(restarted.IsSignedIn);
        Assert.AreEqual("", await settings.ReadSettingAsync<string>(Core.Sources.SettingKeys.TidalRefreshToken));
    }

    // Someone else's answer (another tab, an old sign-in) must not sign in.
    [TestMethod]
    public async Task AnAnswerForAnotherSignInIsRefused()
    {
        var tidal = new FakeTidal();
        var api = NewApi(new TidalParsingTests.DictionarySettings([]), tidal, out var browser);
        browser.State = "not-the-state";

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.LoginAsync(CancellationToken.None));

        Assert.AreEqual(0, tidal.TokenRequests.Count);
        Assert.IsFalse(api.IsSignedIn);
    }

    // Signed out on another device, or the password changed: the saved sign-in is dropped with a clear message.
    [TestMethod]
    public async Task AnExpiredSignInIsForgotten()
    {
        var tidal = new FakeTidal();
        var settings = new TidalParsingTests.DictionarySettings([]);
        await NewApi(settings, tidal, out _).LoginAsync(CancellationToken.None);
        tidal.RefreshFails = true;

        var restarted = NewApi(settings, tidal, out _);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => restarted.SignInAsync());

        StringAssert.Contains(error.Message, "expired");
        Assert.AreEqual("", await settings.ReadSettingAsync<string>(Core.Sources.SettingKeys.TidalRefreshToken));
    }

    // Out of the box: no sign-in, no client ID and secret in Settings, so FluentDL's own app.
    [TestMethod]
    public async Task TheBuiltInAccessIsTheDefault()
    {
        var tidal = new FakeTidal();
        var api = NewApi(new TidalParsingTests.DictionarySettings([]), tidal, out _);

        await api.SignInAsync();

        Assert.AreEqual(TidalAccess.BuiltIn, api.Access);
        Assert.AreEqual("client_credentials", tidal.TokenRequests.Single()["grant_type"]);
        Assert.AreEqual("test-client:test-secret", tidal.TokenRequests.Single()["basic"]);
        Assert.IsNull(api.AccountName);
    }

    // The user's own client ID and secret beat the built-in ones, and a sign-in beats both.
    [TestMethod]
    public async Task ASignInBeatsTheUsersAppWhichBeatsTheBuiltInOne()
    {
        var tidal = new FakeTidal();
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [Core.Sources.SettingKeys.TidalClientId] = "own-client",
            [Core.Sources.SettingKeys.TidalClientSecret] = "own-secret",
        });

        var api = NewApi(settings, tidal, out _);
        await api.SignInAsync();
        Assert.AreEqual(TidalAccess.OwnApp, api.Access);
        Assert.AreEqual("own-client:own-secret", tidal.TokenRequests.Last()["basic"]);

        await api.LoginAsync(CancellationToken.None);
        var restarted = NewApi(settings, tidal, out _);
        await restarted.SignInAsync();
        Assert.AreEqual(TidalAccess.Account, restarted.Access);
        Assert.AreEqual("refresh_token", tidal.TokenRequests.Last()["grant_type"]);

        await restarted.LogoutAsync();
        await restarted.SignInAsync();
        Assert.AreEqual(TidalAccess.OwnApp, restarted.Access, "signing out goes back to the user's app");
    }

    // A build without FluentDL's app (a fork, a PR build) can only use the user's own client ID and secret.
    [TestMethod]
    public async Task ABuildWithoutFluentDLsAppAsksForTheUsersOwn()
    {
        var tidal = new FakeTidal();
        var api = new TidalApi(new TidalParsingTests.DictionarySettings([]), new HttpClient(tidal), Interlocked.Increment(ref nextPort))
        {
            BuiltInClientId = null,
            BuiltInClientSecret = null,
        };

        Assert.IsFalse(api.CanLogin);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.SignInAsync());
        StringAssert.Contains(error.Message, "your own TIDAL client ID");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.LoginAsync(CancellationToken.None));
        Assert.AreEqual(0, tidal.TokenRequests.Count);
    }

    // The built-in secret was reset (it leaked, say): the message points to the sign-in, which doesn't use it.
    [TestMethod]
    public async Task AResetBuiltInSecretPointsToTheSignIn()
    {
        var tidal = new FakeTidal { ClientCredentialsFail = true };
        var api = NewApi(new TidalParsingTests.DictionarySettings([]), tidal, out _);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.SignInAsync());

        StringAssert.Contains(error.Message, "Sign in to TIDAL");
        Assert.AreEqual(TidalAccess.None, api.Access);
        Assert.IsTrue(api.CanLogin);
    }

    private static TidalApi NewApi(TidalParsingTests.DictionarySettings settings, FakeTidal tidal, out FakeBrowser browser)
    {
        var fake = new FakeBrowser();
        browser = fake;
        return new TidalApi(settings, new HttpClient(tidal), Interlocked.Increment(ref nextPort))
        {
            OpenBrowser = fake.OpenAsync,
            BuiltInClientId = "test-client",
            BuiltInClientSecret = "test-secret",
            AccountLoginEnabled = true,
        };
    }

    [TestMethod]
    public async Task AccountSignInIsOffByDefaultAndASavedSignInIsIgnored()
    {
        var tidal = new FakeTidal();
        var settings = new TidalParsingTests.DictionarySettings([]);
        var enabled = NewApi(settings, tidal, out _);
        await enabled.LoginAsync(CancellationToken.None);
        tidal.TokenRequests.Clear();

        var api = new TidalApi(settings, new HttpClient(tidal), Interlocked.Increment(ref nextPort))
        {
            BuiltInClientId = "test-client",
            BuiltInClientSecret = "test-secret",
        };
        Assert.IsFalse(api.CanLogin);
        await api.SignInAsync();
        Assert.AreEqual(TidalAccess.BuiltIn, api.Access);
        Assert.IsTrue(tidal.TokenRequests.All(r => r["grant_type"] == "client_credentials"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.LoginAsync(CancellationToken.None));
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        System.Web.HttpUtility.ParseQueryString(uri.Query).AllKeys.ToDictionary(k => k!, k => System.Web.HttpUtility.ParseQueryString(uri.Query)[k]!);

    // Signs in at once: goes straight to the redirect address with a code and the state it was given.
    private sealed class FakeBrowser
    {
        public Uri? Opened { get; private set; }
        public string? State { get; set; }
        public Task<string>? Page { get; private set; }

        public Task OpenAsync(Uri authorize)
        {
            Opened = authorize;
            var query = Query(authorize);
            var back = $"{query["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(State ?? query["state"])}";
            // Not awaited: the listener only answers once this returns.
            Page = Task.Run(() => new HttpClient().GetStringAsync(back));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTidal : HttpMessageHandler
    {
        public List<Dictionary<string, string>> TokenRequests { get; } = [];
        public bool RefreshFails { get; set; }
        public bool ClientCredentialsFail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "auth.tidal.com")
            {
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
                var fields = form.AllKeys.ToDictionary(k => k!, k => form[k]!);
                if (request.Headers.Authorization is { Scheme: "Basic", Parameter: { } basic })
                {
                    fields["basic"] = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(basic));
                }
                TokenRequests.Add(fields);
                if (fields["grant_type"] == "client_credentials" && ClientCredentialsFail)
                {
                    return Json(System.Net.HttpStatusCode.Unauthorized, """{ "error": "invalid_client", "error_description": "Client authentication failed" }""");
                }
                if (fields["grant_type"] == "refresh_token" && RefreshFails)
                {
                    return Json(System.Net.HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "Refresh token is revoked" }""");
                }
                return Json(System.Net.HttpStatusCode.OK, """{ "access_token": "access", "refresh_token": "refresh-1", "expires_in": 14400, "token_type": "Bearer", "user_id": 7 }""");
            }
            if (request.RequestUri.AbsolutePath == "/v2/users/me")
            {
                return Json(System.Net.HttpStatusCode.OK, """{ "data": { "id": "7", "type": "users", "attributes": { "username": "derek", "email": "catalog@example.test", "country": "GB" } } }""");
            }
            return Json(System.Net.HttpStatusCode.NotFound, """{ "errors": [ { "detail": "not faked" } ] }""");
        }

        private static HttpResponseMessage Json(System.Net.HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    }
}
