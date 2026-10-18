using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Contracts.Sources;

namespace FluentDL.Services.Tidal;

/// <summary>The script's PKCE and stream requests, with a separate saved login for opt-in downloads.</summary>
public sealed class TidalDownloadClient
{
    public const string RedirectUri = "https://tidal.com/android/login/auth";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";
    private const string Scope = "r_usr+w_usr+w_sub";
    private readonly ILocalSettingsService settings;
    private readonly HttpClient http;
    private readonly SemaphoreSlim authLock = new(1, 1);
    private Session? session;
    private DateTimeOffset configChecked;
    private string? configuredClient;
    private string? checkedConfigUrl;
    private string? subscriptionStatus;
    private DateTimeOffset subscriptionChecked;

    internal string? FallbackClientId { get; init; } = BuildValue("TidalDownloadClientId");
    internal string? ConfigUrl { get; init; } = BuildValue("TidalDownloadConfigUrl");
    public TidalDownloadClient(ILocalSettingsService settings) : this(settings, new HttpClient()) { }
    internal TidalDownloadClient(ILocalSettingsService settings, HttpClient http) => (this.settings, this.http) = (settings, http);

    public async Task<bool> HasSessionAsync() => !string.IsNullOrEmpty(await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession));

    /// <summary>The download account identity, saved with the encrypted session. Older sessions recover it on refresh.</summary>
    public async Task<string?> GetAccountNameAsync(CancellationToken token = default) => (await GetSessionAsync(token))?.AccountName;

    /// <summary>TIDAL reports subscription or trial playback access, not whether the account has been charged.</summary>
    public async Task<string> GetSubscriptionStatusAsync(CancellationToken token = default)
    {
        var account = await GetSessionAsync(token);
        if (account == null) return "Not signed in";
        if (account.UserId == null) return "Unavailable";
        if (subscriptionStatus != null && subscriptionChecked > DateTimeOffset.UtcNow.AddMinutes(-1)) return subscriptionStatus;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.tidal.com/v1/users/{Uri.EscapeDataString(account.UserId)}/subscription?countryCode={Uri.EscapeDataString(account.Country)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) return "Unavailable";
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("premiumAccess", out var access) || access.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return "Unavailable";
        if (session?.UserId != account.UserId) return "Unavailable";
        subscriptionStatus = access.GetBoolean() ? "Active" : "Not active";
        subscriptionChecked = DateTimeOffset.UtcNow;
        return subscriptionStatus;
    }

    // A login keeps its verifier and client ID together. Updating the Gist must not change an in-progress login.
    public sealed record Login(Uri Address, string ClientId, string Verifier, string UniqueKey, string State);

    public async Task<Login> BeginLoginAsync(CancellationToken token = default)
    {
        var id = await ClientIdAsync(token);
        var verifier = Url64(RandomNumberGenerator.GetBytes(32));
        var uniqueKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var state = Url64(RandomNumberGenerator.GetBytes(16));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["redirect_uri"] = RedirectUri, ["client_id"] = id,
            ["lang"] = "EN", ["appMode"] = "android", ["client_unique_key"] = uniqueKey,
            ["code_challenge"] = TidalApi.CodeChallenge(verifier), ["code_challenge_method"] = "S256",
            ["restrict_signup"] = "true", ["state"] = state,
        };
        var queryString = string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return new(new Uri("https://login.tidal.com/authorize?" + queryString), id, verifier, uniqueKey, state);
    }

    public static bool IsCallback(Uri uri) => uri.Scheme == "https" && uri.Host == "tidal.com" && uri.AbsolutePath == "/android/login/auth";

    public async Task FinishLoginAsync(Login login, string address, CancellationToken token = default)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || !IsCallback(uri))
            throw new InvalidOperationException("Paste the full address of TIDAL's Page not found page.");
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["state"] != login.State) throw new InvalidOperationException("That address belongs to a different sign-in. Start again.");
        if (query["error"] != null || string.IsNullOrWhiteSpace(query["code"]))
            throw new InvalidOperationException("TIDAL did not return a sign-in code.");
        await authLock.WaitAsync(token);
        try
        {
            using var json = await ExchangeAsync(new()
            {
                ["grant_type"] = "authorization_code", ["client_id"] = login.ClientId, ["code"] = query["code"]!,
                ["redirect_uri"] = RedirectUri, ["code_verifier"] = login.Verifier,
                ["client_unique_key"] = login.UniqueKey, ["scope"] = Scope,
            }, token);
            var root = json.RootElement;
            var country = root.GetProperty("user").GetProperty("countryCode").GetString()!;
            var next = new Session(login.ClientId, root.GetProperty("access_token").GetString()!,
                root.GetProperty("refresh_token").GetString()!,
                DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()), country,
                AccountIdentity(root) ?? "TIDAL account", AccountUserId(root));
            token.ThrowIfCancellationRequested();
            await SaveAsync(next);
            session = next;
            subscriptionStatus = null;
            Serilog.Log.Information("TIDAL download account signed in with PKCE");
        }
        finally { authLock.Release(); }
    }

    public async Task LogoutAsync()
    {
        await authLock.WaitAsync();
        try
        {
            await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, "");
            session = null;
            subscriptionStatus = null;
        }
        finally { authLock.Release(); }
    }

    /// <summary>
    /// Downloads the full track. Missing or expired sign-in throws <see cref="SourceSignInRequiredException"/>.
    /// Playback denial, preview-only access or unsupported audio throws <see cref="TidalDownloadUnavailableException"/>.
    /// </summary>
    public async Task<string> DownloadAsync(string trackId, string pathWithoutExtension, IProgress<ProgressData>? progress,
        CancellationToken token = default)
    {
        var account = await GetSessionAsync(token)
            ?? throw new SourceSignInRequiredException(FluentDL.Core.Sources.SourceIds.Tidal, "Sign in at Settings > Authentication > TIDAL for full downloads.");
        var quality = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadQuality) ?? "HI_RES_LOSSLESS";
        if (quality is not ("LOW" or "HIGH" or "LOSSLESS" or "HI_RES_LOSSLESS")) quality = "HI_RES_LOSSLESS";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.tidal.com/v1/tracks/{Uri.EscapeDataString(trackId)}/playbackinfopostpaywall?countryCode={Uri.EscapeDataString(account.Country)}&audioquality={quality}&playbackmode=STREAM&assetpresentation=FULL");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await LogoutAsync();
            throw new SourceSignInRequiredException(FluentDL.Core.Sources.SourceIds.Tidal, "Your TIDAL account sign-in expired. Sign in again at Settings > Authentication > TIDAL.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new TidalDownloadUnavailableException("TIDAL doesn't allow playing this track on this account or in this country.");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"TIDAL stream request failed: HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (json.RootElement.GetProperty("assetPresentation").GetString() != "FULL")
            throw new TidalDownloadUnavailableException("TIDAL only offers a preview of this track. Full tracks need a TIDAL subscription or trial.");
        TidalDownloadManifest manifest;
        try { manifest = TidalDownloadManifest.Parse(json.RootElement); }
        catch (NotSupportedException)
        {
            throw new TidalDownloadUnavailableException("TIDAL returned audio FluentDL can't save, such as an encrypted stream.");
        }
        var output = pathWithoutExtension + manifest.Extension;
        var overwrite = await settings.ReadSettingAsync<bool>(SettingKeys.Overwrite);
        if (File.Exists(output) && !overwrite) throw new IOException("File already exists.");
        if (!SourceServices.AudioConverter.IsInitialized) throw new InvalidOperationException("FFmpeg is not initialized.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var temporary = pathWithoutExtension + "." + Guid.NewGuid().ToString("N");
        var raw = temporary + ".mp4";
        var remuxed = temporary + manifest.Extension;
        var progressId = ProgressData.NewId();
        try
        {
            await using (var file = File.Create(raw))
            {
                var buffer = new byte[32768];
                for (var i = 0; i < manifest.Parts.Count; i++)
                {
                    using var part = await http.GetAsync(manifest.Parts[i], HttpCompletionOption.ResponseHeadersRead, token);
                    if (!part.IsSuccessStatusCode) throw new HttpRequestException($"TIDAL audio segment failed: HTTP {(int)part.StatusCode}.");
                    var length = part.Content.Headers.ContentLength;
                    await using var stream = await part.Content.ReadAsStreamAsync(token);
                    long partBytes = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, token)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), token);
                        partBytes += read;
                        var partFraction = length > 0 ? Math.Min((double)partBytes / length.Value, 1) : 0;
                        progress?.Report(new ProgressData
                        {
                            uniqueId = progressId, currentBytes = file.Position,
                            fraction = (i + partFraction) / manifest.Parts.Count,
                        });
                    }
                    progress?.Report(new ProgressData
                    {
                        uniqueId = progressId, currentBytes = file.Position, fraction = (double)(i + 1) / manifest.Parts.Count,
                    });
                }
            }
            await SourceServices.AudioConverter.RemuxAudioAsync(raw, remuxed);
            token.ThrowIfCancellationRequested();
            File.Move(remuxed, output, overwrite);
            Serilog.Log.Information("TIDAL direct download completed; requested {RequestedQuality}, received {Quality}", quality, manifest.Quality);
            return output;
        }
        finally
        {
            File.Delete(raw);
            File.Delete(remuxed);
        }
    }

    // Catalog requests share the download session, including its original OAuth client and refresh-token storage.
    // A rejected access token forces one refresh, unless another caller already refreshed it.
    internal Task<Session?> GetApiSessionAsync(CancellationToken token, string? rejectedAccessToken = null) =>
        GetSessionAsync(token, rejectedAccessToken);

    private async Task<Session?> GetSessionAsync(CancellationToken token, string? rejectedAccessToken = null)
    {
        await authLock.WaitAsync(token);
        try
        {
            if (session == null)
            {
                var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
                if (string.IsNullOrEmpty(saved)) return null;
                try
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    var bytes = ProtectedData.Unprotect(Convert.FromBase64String(saved), null, DataProtectionScope.CurrentUser);
                    session = JsonSerializer.Deserialize<Session>(bytes);
                }
                catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
                {
                    await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, "");
                    return null;
                }
            }
            if (session == null || session.Expires > DateTimeOffset.UtcNow.AddMinutes(5)
                && !string.IsNullOrEmpty(session.AccountName) && session.AccessToken != rejectedAccessToken) return session;
            try
            {
                // Refresh under the original client, even if the Gist has since changed.
                using var json = await ExchangeAsync(new()
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = session.ClientId,
                    ["refresh_token"] = session.RefreshToken, ["scope"] = Scope,
                }, token);
                var root = json.RootElement;
                var next = session with
                {
                    AccessToken = root.GetProperty("access_token").GetString()!,
                    RefreshToken = root.TryGetProperty("refresh_token", out var r) && !string.IsNullOrEmpty(r.GetString()) ? r.GetString()! : session.RefreshToken,
                    Expires = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
                    AccountName = AccountIdentity(root) ?? session.AccountName ?? "TIDAL account",
                    UserId = AccountUserId(root) ?? session.UserId,
                };
                await SaveAsync(next);
                session = next;
            }
            catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                session = null;
                await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, "");
                Serilog.Log.Warning("TIDAL download sign-in expired; using an equivalent until signed in again");
            }
            return session;
        }
        finally { authLock.Release(); }
    }

    private async Task<JsonDocument> ExchangeAsync(Dictionary<string, string> form, CancellationToken token)
    {
        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"TIDAL download sign-in failed: HTTP {(int)response.StatusCode}. Try signing in again.", null, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private Task SaveAsync(Session value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), null, DataProtectionScope.CurrentUser);
        return settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, Convert.ToBase64String(bytes));
    }

    private async Task<string> ClientIdAsync(CancellationToken token)
    {
        var manual = (await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadClientId))?.Trim();
        if (!string.IsNullOrEmpty(manual))
        {
            if (!IsValidClientId(manual)) throw new InvalidOperationException("The download client ID in Settings isn't valid.");
            Serilog.Log.Information("TIDAL download client configuration source: Settings");
            return manual;
        }
        var url = ConfigUrl;
        if (url == checkedConfigUrl && configuredClient != null && configChecked > DateTimeOffset.UtcNow.AddHours(-1)) return configuredClient;
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "gist.githubusercontent.com")
                throw new InvalidOperationException("Use a raw HTTPS Gist URL for the download client configuration.");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var text = await http.GetStringAsync(uri, timeout.Token);
                using var json = JsonDocument.Parse(text);
                var id = json.RootElement.GetProperty("client_id").GetString();
                if (!IsValidClientId(id)) throw new JsonException();
                await settings.SaveSettingAsync(SettingKeys.TidalDownloadCachedClient, id);
                configuredClient = id;
                checkedConfigUrl = url;
                configChecked = DateTimeOffset.UtcNow;
                Serilog.Log.Information("TIDAL download client configuration source: Gist");
                return id!;
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException || e is OperationCanceledException && !token.IsCancellationRequested)
            {
                Serilog.Log.Information("TIDAL download configuration unavailable; using cached or bundled client ID");
            }
        }
        var cached = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadCachedClient);
        var fallback = IsValidClientId(cached) ? cached : FallbackClientId;
        if (!IsValidClientId(fallback)) throw new InvalidOperationException("No download client configured. Enter a download client ID in Settings or supply TIDAL_DOWNLOAD_CLIENT_ID when building.");
        Serilog.Log.Information("TIDAL download client configuration source: {Source}", IsValidClientId(cached) ? "Cache" : "Bundled");
        return fallback!;
    }

    public static bool IsValidClientId(string? id) => id is { Length: > 0 and <= 128 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static string Url64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string? BuildValue(string key) => typeof(TidalDownloadClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value;
    private static string? AccountIdentity(JsonElement response)
    {
        if (!response.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object) return null;
        foreach (var field in new[] { "email", "username" })
            if (user.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();
        return null;
    }
    private static string? AccountUserId(JsonElement response)
    {
        if (response.TryGetProperty("user_id", out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (response.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object && user.TryGetProperty("userId", out value)
            && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return null;
    }
    internal sealed record Session(string ClientId, string AccessToken, string RefreshToken, DateTimeOffset Expires, string Country,
        string? AccountName = null, string? UserId = null);
}

/// <summary>TIDAL can't provide the full track. The message says why and what to do.</summary>
public sealed class TidalDownloadUnavailableException(string message) : Exception(message);
