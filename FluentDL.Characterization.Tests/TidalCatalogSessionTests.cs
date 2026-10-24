using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FluentDL.Core.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class TidalCatalogSessionTests
{
    [TestMethod]
    public async Task UninitializedExpiryCannotUnderflowDuringConcurrentTokenPublication()
    {
        var settings = Settings();
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);
        // Reproduce a caller observing the token before its expiry has been initialized.
        typeof(TidalApi).GetField("token", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(api, "partially-published-token");

        await api.GetAsync("tracks/1", CancellationToken.None);

        Assert.AreEqual("app", http.Catalog.Single().Token);
        Assert.AreEqual("client_credentials", http.Grants.Single()["grant_type"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreviewUsesAnIsolatedAppTokenWithoutRefreshingOrChangingTheDownloadSession(bool expired)
    {
        var settings = Settings();
        await SaveSession(settings, expired);
        var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);
        var source = new FluentDL.Services.Sources.TidalSource(api);

        var uri = await source.GetPreviewAudioAsync(new() { Source = SourceIds.Tidal, Id = "1" });

        Assert.AreEqual("https://preview.example.test/30-seconds.m3u8", uri);
        Assert.AreEqual("app", http.Catalog.Single().Token);
        Assert.AreEqual("client_credentials", http.Grants.Single()["grant_type"]);
        Assert.AreEqual(saved, await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession));
        Assert.AreEqual(TidalAccess.None, api.Access, "Loading a preview must not change the main client's account state.");
    }

    [TestMethod]
    public async Task PreviewPreservesAccountIdentityAndCountryAndCatalogStillUsesTheAccount()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);
        await api.SignInAsync();

        await api.GetPreviewManifestAsync("1", CancellationToken.None);
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
        Assert.AreEqual("account@example.test", api.AccountEmail);
        StringAssert.EndsWith(http.Catalog[0].Url, "countryCode=GB");
        await api.GetAsync("tracks/1", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "app", "download" }, http.Catalog.Select(request => request.Token).ToArray());
        Assert.AreEqual(1, http.Grants.Count);
    }

    [TestMethod]
    public async Task PreviewUsesBundledCredentialsAndSharesOneTokenAcrossConcurrentRequests()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        await SaveSession(settings);
        var http = new FakeTidal();
        var api = new TidalApi(settings, new HttpClient(http)) { BuiltInClientId = "bundled-client", BuiltInClientSecret = "bundled-secret" };

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => api.GetPreviewManifestAsync(i.ToString(), CancellationToken.None)));

        Assert.AreEqual(1, http.Grants.Count);
        Assert.IsTrue(http.Catalog.All(request => request.Token == "app"));
        Assert.AreEqual(12, http.Catalog.Count);
    }

    [TestMethod]
    public async Task ReconnectingInvalidatesThePreviewTokenWithoutClearingTheDownloadLogin()
    {
        var settings = Settings();
        await SaveSession(settings);
        var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);
        await api.GetPreviewManifestAsync("1", CancellationToken.None);

        await api.SignInAsync();
        await api.GetPreviewManifestAsync("1", CancellationToken.None);

        Assert.AreEqual(2, http.Grants.Count);
        Assert.IsTrue(http.Grants.All(grant => grant["grant_type"] == "client_credentials"));
        Assert.AreEqual(saved, await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession));
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
    }

    [TestMethod]
    public async Task PreviewNeverUsesTheSeparateApiAccountRefreshTokenEither()
    {
        var settings = Settings();
        var saved = Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes("api-refresh"), null, DataProtectionScope.CurrentUser));
        await settings.SaveSettingAsync(SettingKeys.TidalRefreshToken, saved);
        var http = new FakeTidal { AppRefreshReturned = true };
        var api = new TidalApi(settings, new HttpClient(http))
        {
            AccountLoginEnabled = true, BuiltInClientId = "bundled-client", BuiltInClientSecret = "bundled-secret",
        };

        await api.GetPreviewManifestAsync("1", CancellationToken.None);

        Assert.AreEqual("client_credentials", http.Grants.Single()["grant_type"]);
        Assert.AreEqual(saved, await settings.ReadSettingAsync<string>(SettingKeys.TidalRefreshToken));
        Assert.AreEqual(TidalAccess.None, api.Access);
    }

    [TestMethod]
    public async Task PreviewWithoutAppCredentialsFailsRatherThanUsingTheAccountForFullPlayback()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        await SaveSession(settings);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.GetPreviewManifestAsync("1", CancellationToken.None));

        Assert.AreEqual(0, http.Catalog.Count);
        Assert.AreEqual(0, http.Grants.Count);
        Assert.IsFalse(string.IsNullOrEmpty(await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession)));
    }

    [TestMethod]
    [DataRow("FULL")]
    [DataRow("")]
    public async Task PreviewRefusesFullOrUnknownPresentations(string presentation)
    {
        var settings = Settings();
        var http = new FakeTidal { ManifestPresentation = presentation };
        var (api, _) = Clients(settings, http);
        var source = new FluentDL.Services.Sources.TidalSource(api);

        Assert.IsNull(await source.GetPreviewAudioAsync(new() { Source = SourceIds.Tidal, Id = "1" }));
    }

    [TestMethod]
    public async Task PreviewAuthorizationFailureRetriesOnlyAppTokensAndLeavesTheAccountIntact()
    {
        var settings = Settings();
        await SaveSession(settings);
        var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
        var http = new FakeTidal { CatalogResponse = _ => HttpStatusCode.Unauthorized };
        var (api, _) = Clients(settings, http);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => api.GetPreviewManifestAsync("1", CancellationToken.None));

        Assert.AreEqual(2, http.Catalog.Count);
        Assert.IsTrue(http.Catalog.All(request => request.Token == "app"));
        Assert.AreEqual(2, http.Grants.Count);
        Assert.IsTrue(http.Grants.All(grant => grant["grant_type"] == "client_credentials"));
        Assert.AreEqual(saved, await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession));
    }

    [TestMethod]
    public async Task MissingPreviewReturnsNullWithoutSwitchingToAccountPlayback()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal { CatalogResponse = _ => HttpStatusCode.NotFound };
        var (api, _) = Clients(settings, http);

        Assert.IsNull(await new FluentDL.Services.Sources.TidalSource(api).GetPreviewAudioAsync(new() { Source = SourceIds.Tidal, Id = "1" }));
        Assert.AreEqual("app", http.Catalog.Single().Token);
    }

    [TestMethod]
    [DataRow(null, "GB")]
    [DataRow("US", "US")]
    public async Task DownloadAccountBeatsAppCredentialsAndSuppliesTheCountry(string? country, string expected)
    {
        var settings = Settings();
        await SaveSession(settings);
        if (country != null) await settings.SaveSettingAsync(SettingKeys.TidalCountry, country);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);

        await api.SignInAsync();
        await api.GetAsync("tracks/1", CancellationToken.None);

        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
        Assert.IsTrue(api.IsSignedIn);
        Assert.IsTrue(new FluentDL.Services.Sources.TidalSource(api).IsConnected);
        Assert.AreEqual(expected, api.CountryCode);
        Assert.AreEqual("account@example.test", api.AccountEmail);
        Assert.AreEqual(0, http.Grants.Count);
        Assert.AreEqual(1, http.Catalog.Count);
        Assert.AreEqual("download", http.Catalog[0].Token);
        StringAssert.EndsWith(http.Catalog[0].Url, "countryCode=" + expected);
    }

    [TestMethod]
    public async Task SigningInAndOutSwitchesTheNextRequestWithoutRestarting()
    {
        var settings = Settings();
        var http = new FakeTidal();
        var (api, downloads) = Clients(settings, http);
        await api.GetAsync("tracks/1", CancellationToken.None);

        // The same client that the login dialog uses, not a second session cached by the catalog.
        var login = new TidalDownloadClient.Login(new Uri("https://login.tidal.com"), "download-client", "verifier", "key", "state");
        await downloads.FinishLoginAsync(login, TidalDownloadClient.RedirectUri + "?code=code&state=state");
        await api.GetAsync("tracks/1", CancellationToken.None);
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);

        await downloads.LogoutAsync();
        await api.GetAsync("tracks/1", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "app", "renewed-download", "app" }, http.Catalog.Select(r => r.Token).ToArray());
        Assert.AreEqual(TidalAccess.OwnApp, api.Access);
        Assert.IsNull(api.AccountName);
        Assert.IsNull(api.AccountEmail);
        Assert.AreEqual(2, http.Grants.Count(g => g["grant_type"] == "client_credentials"));
    }

    [TestMethod]
    public async Task ExpiryRefreshesUnderTheOriginalDownloadClientAndPersistsRotation()
    {
        var settings = Settings();
        await SaveSession(settings, expired: true);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);

        await api.GetAsync("tracks/1", CancellationToken.None);

        var grant = http.Grants.Single();
        Assert.AreEqual("download-client", grant["client_id"]);
        Assert.AreEqual("refresh_token", grant["grant_type"]);
        Assert.AreEqual("old-refresh", grant["refresh_token"]);
        Assert.AreEqual("r_usr+w_usr+w_sub", grant["scope"]);
        var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
        Assert.IsFalse(saved!.Contains("rotated-refresh"));
        var restored = JsonSerializer.Deserialize<TidalDownloadClient.Session>(ProtectedData.Unprotect(Convert.FromBase64String(saved), null, DataProtectionScope.CurrentUser));
        Assert.AreEqual("rotated-refresh", restored!.RefreshToken);
        Assert.AreEqual("download-client", restored.ClientId);
        Assert.AreEqual("renewed-download", http.Catalog.Single().Token);
    }

    [TestMethod]
    public async Task ConcurrentCatalogRequestsShareOneDownloadRefresh()
    {
        var settings = Settings();
        await SaveSession(settings, expired: true);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => api.GetAsync($"tracks/{i}", CancellationToken.None)));

        Assert.AreEqual(1, http.Grants.Count);
        Assert.AreEqual(20, http.Catalog.Count);
        Assert.IsTrue(http.Catalog.All(r => r.Token == "renewed-download"));
    }

    [TestMethod]
    public async Task SignedOutAccountsStillUseBundledCatalogAccessWhenNoOwnAppIsConfigured()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var http = new FakeTidal();
        var downloads = new TidalDownloadClient(settings, new HttpClient(http));
        var api = new TidalApi(settings, new HttpClient(http), downloadClient: downloads)
            { BuiltInClientId = "bundled-client", BuiltInClientSecret = "bundled-secret" };

        await api.GetAsync("tracks/1", CancellationToken.None);

        Assert.AreEqual(TidalAccess.BuiltIn, api.Access);
        Assert.AreEqual("client_credentials", http.Grants.Single()["grant_type"]);
        Assert.AreEqual("app", http.Catalog.Single().Token);
    }

    [TestMethod]
    public async Task RevokedRefreshFallsBackToAppAccessAndClearsAccountIdentity()
    {
        var settings = Settings();
        await SaveSession(settings, expired: true);
        var http = new FakeTidal { RefreshStatus = HttpStatusCode.BadRequest };
        var (api, downloads) = Clients(settings, http);

        await api.SignInAsync();
        await api.GetAsync("tracks/1", CancellationToken.None);

        Assert.AreEqual(TidalAccess.OwnApp, api.Access);
        Assert.IsFalse(await downloads.HasSessionAsync());
        Assert.IsNull(api.AccountName);
        Assert.AreEqual("app", http.Catalog.Single().Token);
    }

    [TestMethod]
    public async Task UnauthorizedCatalogRequestRefreshesOnceBeforeRetrying()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal { CatalogResponse = bearer => bearer == "download" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK };
        var (api, _) = Clients(settings, http);

        await api.GetAsync("tracks/1", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "download", "renewed-download" }, http.Catalog.Select(r => r.Token).ToArray());
        Assert.AreEqual(1, http.Grants.Count);
        Assert.AreEqual("refresh_token", http.Grants.Single()["grant_type"]);
        Assert.AreEqual(TidalAccess.DownloadAccount, api.Access);
    }

    [TestMethod]
    public async Task CatalogRejectionAfterRefreshDoesNotSignOutTheDownloadAccount()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal { CatalogResponse = bearer => bearer == "app" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized };
        var (api, downloads) = Clients(settings, http);

        await api.GetAsync("tracks/1", CancellationToken.None);
        await api.GetAsync("tracks/2", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "download", "renewed-download", "app", "app" }, http.Catalog.Select(r => r.Token).ToArray());
        Assert.AreEqual(TidalAccess.OwnApp, api.Access);
        Assert.IsTrue(await downloads.HasSessionAsync());
        Assert.AreEqual("account@example.test", await downloads.GetAccountNameAsync());
        Assert.AreEqual(1, http.Grants.Count(g => g["grant_type"] == "refresh_token"));
    }

    [TestMethod]
    public async Task ForbiddenDoesNotSwitchCredentials()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal { CatalogResponse = _ => HttpStatusCode.Forbidden };
        var (api, downloads) = Clients(settings, http);

        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => api.GetAsync("tracks/1", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.AreEqual(0, http.Grants.Count);
        Assert.AreEqual(1, http.Catalog.Count);
        Assert.IsTrue(await downloads.HasSessionAsync());
    }

    [TestMethod]
    public async Task RateLimitingRetriesTheSameAccountWithoutRequestingAppCredentials()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal { CatalogResponse = _ => HttpStatusCode.TooManyRequests };
        var (api, _) = Clients(settings, http);

        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => api.GetAsync("tracks/1", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.AreEqual(4, http.Catalog.Count);
        Assert.IsTrue(http.Catalog.All(r => r.Token == "download"));
        Assert.AreEqual(0, http.Grants.Count);
    }

    [TestMethod]
    public async Task TemporaryRefreshFailureDoesNotDiscardTheAccountOrSwitchCredentials()
    {
        var settings = Settings();
        await SaveSession(settings, expired: true);
        var http = new FakeTidal { RefreshStatus = HttpStatusCode.ServiceUnavailable };
        var (api, downloads) = Clients(settings, http);

        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => api.GetAsync("tracks/1", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.IsTrue(await downloads.HasSessionAsync());
        Assert.AreEqual(0, http.Catalog.Count);
        Assert.AreEqual(1, http.Grants.Count);
    }

    [TestMethod]
    public async Task CancellationDoesNotFallBackToAnotherCredential()
    {
        var settings = Settings();
        await SaveSession(settings);
        var http = new FakeTidal();
        var (api, _) = Clients(settings, http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => api.GetAsync("tracks/1", cancellation.Token));
        Assert.AreEqual(0, http.Grants.Count);
        Assert.AreEqual(0, http.Catalog.Count);
    }

    private static TidalParsingTests.DictionarySettings Settings() => new(new()
    {
        [SettingKeys.TidalClientId] = "own-client",
        [SettingKeys.TidalClientSecret] = "own-secret",
    });

    private static Task SaveSession(TidalParsingTests.DictionarySettings settings, bool expired = false)
    {
        var session = new TidalDownloadClient.Session("download-client", "download", "old-refresh",
            DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), "GB", "account@example.test", "7");
        return settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, Convert.ToBase64String(
            ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session), null, DataProtectionScope.CurrentUser)));
    }

    private static (TidalApi Api, TidalDownloadClient Downloads) Clients(TidalParsingTests.DictionarySettings settings, FakeTidal http)
    {
        var downloads = new TidalDownloadClient(settings, new HttpClient(http));
        var api = new TidalApi(settings, new HttpClient(http), downloadClient: downloads) { BuiltInClientId = null, BuiltInClientSecret = null };
        return (api, downloads);
    }

    private sealed class FakeTidal : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Grants { get; } = [];
        public List<(string Url, string? Token)> Catalog { get; } = [];
        public HttpStatusCode RefreshStatus { get; init; } = HttpStatusCode.OK;
        public Func<string?, HttpStatusCode> CatalogResponse { get; init; } = _ => HttpStatusCode.OK;
        public string ManifestPresentation { get; init; } = "PREVIEW";
        public bool AppRefreshReturned { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "auth.tidal.com")
            {
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
                lock (Grants) Grants.Add(form.AllKeys.ToDictionary(k => k!, k => form[k]!));
                await Task.Yield(); // Let concurrent requests contend for the shared session refresh.
                if (form["grant_type"] == "client_credentials") return Json(HttpStatusCode.OK, AppRefreshReturned
                    ? """{"access_token":"app","expires_in":14400,"refresh_token":"must-not-replace-account-token"}"""
                    : """{"access_token":"app","expires_in":14400}""");
                return Json(RefreshStatus, """{"access_token":"renewed-download","refresh_token":"rotated-refresh","expires_in":14400,"user":{"userId":7,"email":"account@example.test","countryCode":"GB"}}""");
            }
            var bearer = request.Headers.Authorization?.Parameter;
            lock (Catalog) Catalog.Add((request.RequestUri.ToString(), bearer));
            var body = request.RequestUri.AbsolutePath.Contains("/trackManifests/")
                ? JsonSerializer.Serialize(new { data = new { id = "1", type = "trackManifests", attributes = new { trackPresentation = bearer == "app" ? ManifestPresentation : "FULL", uri = "https://preview.example.test/30-seconds.m3u8" } } })
                : """{"data":{"id":"1","type":"tracks","attributes":{}}}""";
            var response = Json(CatalogResponse(bearer), body);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    }
}
