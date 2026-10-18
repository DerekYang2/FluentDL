using System.Net;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class SpotifySessionTests
{
    private const string ClientId = "synthetic-client";
    private const string Secret = "synthetic-secret";
    private static readonly string Scopes = string.Join(" ", SpotifyUserAuthorization.PlaylistScopes);
    private static SpotifyUserSessionStore.Session Saved(string refresh = "synthetic-refresh") =>
        new(ClientId, SpotifyUserSessionStore.CredentialsHash(ClientId, Secret), "synthetic-user", refresh, Scopes);
    private static AuthorizationCodeRefreshResponse Refreshed(string? refresh = null) => new()
    {
        AccessToken = "synthetic-access", RefreshToken = refresh!, ExpiresIn = 3600, CreatedAt = DateTime.UtcNow, Scope = null!, TokenType = "Bearer",
    };

    [TestMethod]
    public async Task SessionRoundTripsThroughDpapiWithoutPlaintextTokensOrSecret()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        Assert.IsFalse(settings.Value.Contains("synthetic", StringComparison.Ordinal));
        Assert.IsTrue(Convert.FromBase64String(settings.Value).Length > 0);
        var restartedStore = new SpotifyUserSessionStore(settings);
        Assert.AreEqual(Saved(), await restartedStore.ReadAsync(ClientId, Secret, () => true));
    }

    [TestMethod]
    [DataRow("different-client", Secret)]
    [DataRow(ClientId, "different-secret")]
    public async Task ChangingEitherCredentialDiscardsTheSavedSession(string id, string secret)
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        Assert.IsNull(await store.ReadAsync(id, secret, () => true));
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    [DataRow("invalid base64")]
    [DataRow("bm90LWRwYXBp")]
    public async Task CorruptSavedTokensAreDiscarded(string saved)
    {
        var settings = new MemorySettings { Value = saved };
        Assert.IsNull(await new SpotifyUserSessionStore(settings).ReadAsync(ClientId, Secret, () => true));
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task OldGenerationCannotResaveAfterSignOut()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        await store.ClearAsync();
        await store.SaveAsync(Saved("late-refresh"), () => false);
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task RestartRefreshesAndVerifiesTheSameAccountWithoutBrowserAuthorization()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        var source = Source(settings);
        int refreshes = 0, profiles = 0;
        source.RefreshUserTokenAsync = (id, secret, refresh, _) =>
        {
            Assert.AreEqual(ClientId, id);
            Assert.AreEqual(Secret, secret);
            Assert.AreEqual(Saved().RefreshToken, refresh);
            refreshes++;
            return Task.FromResult(Refreshed("rotated-refresh"));
        };
        source.VerifyUserAccountAsync = async (auth, token) =>
        {
            Assert.AreEqual("synthetic-access", await auth.GetAccessTokenAsync(token));
            profiles++;
            return "synthetic-user";
        };
        await source.RestoreUserAuthorizationAsync(ClientId, Secret);
        Assert.IsTrue(source.IsSignedInForPlaylists);
        Assert.AreEqual("synthetic-user", source.UserAccountId);
        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(1, profiles);
        Assert.AreEqual("rotated-refresh", (await store.ReadAsync(ClientId, Secret, () => true))!.RefreshToken);
        await source.ClearUserAuthorizationAsync();
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task ProfileMustMatchBeforePublishingTheRestoredSession()
    {
        var settings = new MemorySettings();
        await new SpotifyUserSessionStore(settings).SaveAsync(Saved(), () => true);
        var source = Source(settings);
        source.VerifyUserAccountAsync = (_, _) => Task.FromResult("different-user");
        await source.RestoreUserAuthorizationAsync(ClientId, Secret);
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task StartupAndSavingUnchangedCredentialsKeepTheRestoredPrivateReadClient()
    {
        var settings = new MemorySettings();
        await new SpotifyUserSessionStore(settings).SaveAsync(Saved(), () => true);
        var source = Source(settings);
        source.CheckCatalogCredentialsAsync = () => Task.FromResult(true);
        int refreshes = 0;
        source.RefreshUserTokenAsync = (_, _, _, _) => { refreshes++; return Task.FromResult(Refreshed()); };
        object? firstClient = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await source.Initialize(ClientId, Secret);
            Assert.IsTrue(source.IsConnected);
            Assert.IsTrue(source.IsSignedInForPlaylists);
            var client = typeof(SpotifySource).GetField("spotifyUser",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source);
            Assert.IsNotNull(client,
                "Catalog initialization must not discard the restored private-playlist client.");
            if (attempt == 0) firstClient = client;
            else Assert.AreSame(firstClient, client, "Saving unchanged credentials must not race an existing account's token refresh.");
        }
        Assert.AreEqual(1, refreshes);
    }

    [TestMethod]
    public async Task SignOutDuringRestoreCannotPublishOrResaveTheLateResponse()
    {
        var settings = new MemorySettings();
        await new SpotifyUserSessionStore(settings).SaveAsync(Saved(), () => true);
        var source = Source(settings);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<AuthorizationCodeRefreshResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.RefreshUserTokenAsync = (_, _, _, _) => { entered.SetResult(); return reply.Task; };
        var restore = source.RestoreUserAuthorizationAsync(ClientId, Secret);
        await entered.Task;
        await source.ClearUserAuthorizationAsync();
        reply.SetResult(Refreshed("late-rotation"));
        await restore;
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task TransientProfileFailureRetainsTheRotatedRefreshTokenForNextStartup()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        var source = Source(settings);
        source.RefreshUserTokenAsync = (_, _, _, _) => Task.FromResult(Refreshed("rotated-before-profile"));
        source.VerifyUserAccountAsync = (_, _) => throw new HttpRequestException("Synthetic offline response");
        await source.RestoreUserAuthorizationAsync(ClientId, Secret);
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual("rotated-before-profile", (await store.ReadAsync(ClientId, Secret, () => true))!.RefreshToken);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, true)]
    [DataRow(HttpStatusCode.Unauthorized, true)]
    [DataRow(HttpStatusCode.Forbidden, true)]
    [DataRow(HttpStatusCode.TooManyRequests, false)]
    [DataRow(HttpStatusCode.ServiceUnavailable, false)]
    public async Task RestoreDiscardsRejectedTokensButRetainsThemAfterTransientErrors(HttpStatusCode status, bool discard)
    {
        var settings = new MemorySettings();
        await new SpotifyUserSessionStore(settings).SaveAsync(Saved(), () => true);
        var source = Source(settings);
        source.RefreshUserTokenAsync = (_, _, _, _) => throw new APIException(new Response(new Dictionary<string, string>()) { StatusCode = status });
        await source.RestoreUserAuthorizationAsync(ClientId, Secret);
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual(discard, settings.Value.Length == 0);
    }

    [TestMethod]
    public async Task MissingCredentialsAndRevokedScopesNeverRestoreAnAccount()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        await store.SaveAsync(Saved(), () => true);
        var source = Source(settings);
        await source.RestoreUserAuthorizationAsync(null, null);
        Assert.AreEqual("", settings.Value);
        await store.SaveAsync(Saved(), () => true);
        source.RefreshUserTokenAsync = (_, _, _, _) =>
        {
            var reply = Refreshed();
            reply.Scope = "playlist-read-private";
            return Task.FromResult(reply);
        };
        await source.RestoreUserAuthorizationAsync(ClientId, Secret);
        Assert.IsFalse(source.IsSignedInForPlaylists);
        Assert.AreEqual("", settings.Value);
    }

    [TestMethod]
    public async Task AutomaticRefreshPersistsRotationBeforeReturningTheAccessToken()
    {
        var settings = new MemorySettings();
        var store = new SpotifyUserSessionStore(settings);
        var initial = new AuthorizationCodeTokenResponse
        {
            AccessToken = "expired", RefreshToken = "old-refresh", CreatedAt = DateTime.UtcNow.AddHours(-2), ExpiresIn = 1, Scope = Scopes,
        };
        var auth = new SpotifyUserAuthorization(initial, _ => Task.FromResult(Refreshed("new-refresh")),
            response => store.SaveAsync(Saved(response.RefreshToken), () => true));
        Assert.AreEqual("synthetic-access", await auth.GetAccessTokenAsync(CancellationToken.None));
        Assert.AreEqual("new-refresh", (await store.ReadAsync(ClientId, Secret, () => true))!.RefreshToken);
    }

    private static SpotifySource Source(MemorySettings settings) => new(settings, null!, null!)
    {
        RefreshUserTokenAsync = (_, _, _, _) => Task.FromResult(Refreshed()),
        VerifyUserAccountAsync = (_, _) => Task.FromResult("synthetic-user"),
    };

    [TestMethod]
    public async Task FailedRotationSaveIsRetriedWithoutRefreshingOrReplayingAnApiRequest()
    {
        int refreshes = 0, saves = 0;
        var response = new AuthorizationCodeTokenResponse
        {
            AccessToken = "expired", RefreshToken = "old-refresh", CreatedAt = DateTime.UtcNow.AddHours(-2), ExpiresIn = 1, Scope = Scopes,
        };
        var auth = new SpotifyUserAuthorization(response, _ =>
        {
            refreshes++;
            return Task.FromResult(Refreshed("rotated-refresh"));
        }, updated =>
        {
            Assert.AreEqual("rotated-refresh", updated.RefreshToken);
            if (++saves == 1) throw new IOException("Synthetic storage failure");
            return Task.CompletedTask;
        });
        await Assert.ThrowsExceptionAsync<IOException>(() => auth.GetAccessTokenAsync(CancellationToken.None));
        Assert.AreEqual("synthetic-access", await auth.GetAccessTokenAsync(CancellationToken.None));
        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(2, saves);
    }

    private sealed class MemorySettings : ILocalSettingsService
    {
        internal string Value = "";
        public Task<T?> ReadSettingAsync<T>(string key)
        {
            Assert.AreEqual(SettingKeys.SpotifyUserSession, key);
            return Task.FromResult((T?)(object)Value);
        }
        public Task SaveSettingAsync<T>(string key, T value)
        {
            Assert.AreEqual(SettingKeys.SpotifyUserSession, key);
            Value = (string)(object)value!;
            return Task.CompletedTask;
        }
        public Task<string> ExportSettingsAsync() => throw new NotSupportedException();
        public Task<string?> ImportSettingsAsync(string json) => throw new NotSupportedException();
        public Task ClearSettings() => throw new NotSupportedException();
    }
}
