using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;

namespace FluentDL.Services.Tidal;

/// <summary>How FluentDL gets into TIDAL, in the order it tries them.</summary>
public enum TidalAccess
{
    None,

    /// <summary>The user's TIDAL account: a one-time sign-in in the browser, saved.</summary>
    Account,

    /// <summary>The user's own developer app's client ID and secret, from Settings.</summary>
    OwnApp,

    /// <summary>FluentDL's own client ID and secret, built into release builds. No setup.</summary>
    BuiltIn,
}

/// <summary>
/// Requests to TIDAL's official API (openapi.tidal.com/v2). It tries, in order:
/// <list type="number">
/// <item>The user's TIDAL account: a one-time sign-in in the browser with FluentDL's TIDAL app (authorization code
/// with PKCE, which needs no secret). The refresh token is saved encrypted for the Windows user, so later starts sign
/// in without the browser.</item>
/// <item>The user's own developer app: its client ID and secret from Settings (client credentials, no account).</item>
/// <item>FluentDL's TIDAL app with its secret, when the build has it (client credentials, no account). If the secret
/// leaks and is reset, the sign-in still works, since it doesn't use the secret.</item>
/// </list>
/// Tokens last 4 hours; a new one is fetched shortly before that. Every request carries a country (the TIDAL country
/// setting, else the account's country, else the Windows region), which decides availability and ranking.
/// </summary>
public sealed class TidalApi
{
    internal const int LoginPort = 5544;
    private const string LoginScopes = "user.read"; // the account's name and country
    private const string ApiBase = "https://openapi.tidal.com/v2/";
    private const string AuthorizeUrl = "https://login.tidal.com/authorize";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient http;
    private readonly ILocalSettingsService settings;
    private readonly int loginPort;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;
    private string? accountCountry;

    public TidalApi(ILocalSettingsService settings)
        : this(settings, new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }))
    {
    }

    /// <param name="loginPort">Tests use another port, so a running FluentDL doesn't get in the way.</param>
    internal TidalApi(ILocalSettingsService settings, HttpClient http, int loginPort = LoginPort)
    {
        this.settings = settings;
        this.http = http;
        this.loginPort = loginPort;
        CountryCode = ResolveCountry(null, RegionInfo.CurrentRegion.TwoLetterISORegionName);
    }

    /// <summary>
    /// The country of the last sign-in or request. It changes what TIDAL offers and how it ranks results (Canada ranks another
    /// Random Access Memories edition second than the US).
    /// </summary>
    public string CountryCode { get; private set; }

    public bool IsSignedIn => token != null;

    /// <summary>How the current token was obtained.</summary>
    public TidalAccess Access { get; private set; }

    /// <summary>
    /// FluentDL's app in TIDAL's developer dashboard, where http://127.0.0.1:5544/callback is registered. The build
    /// supplies it (see FluentDL.Sources.csproj); without it there is no sign-in. Tests set their own.
    /// </summary>
    internal string? BuiltInClientId { get; init; } = BuildValue("TidalClientId");

    /// <summary>The secret of FluentDL's app, when the build supplies one. Without it there is no built-in access.</summary>
    internal string? BuiltInClientSecret { get; init; } = BuildValue("TidalClientSecret");

    /// <summary>Whether this build can sign in to a TIDAL account.</summary>
    public bool CanLogin => BuiltInClientId != null;

    /// <summary>The TIDAL account's username while signed in to an account, else null.</summary>
    public string? AccountName { get; private set; }

    /// <summary>The account email returned by user.read, displayed locally in Settings.</summary>
    public string? AccountEmail { get; private set; }

    /// <summary>Opens the sign-in page. Tests replace it with a fake browser.</summary>
    internal Func<Uri, Task> OpenBrowser { get; set; } = uri =>
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    };

    private string RedirectUri => $"http://127.0.0.1:{loginPort}/callback";

    /// <summary>
    /// Gets a fresh token: from the saved account sign-in when there is one, else with the user's client ID and
    /// secret, else with the built-in ones. Throws with the reason when none works.
    /// </summary>
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            token = null;
            await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            tokenLock.Release();
        }
        await ReadAccountAsync(cancellationToken);
        await ReadCountryAsync();
    }

    /// <summary>
    /// Signs in to the user's TIDAL account: opens TIDAL's sign-in page in the browser and waits up to 5 minutes for it
    /// to come back to http://127.0.0.1:5544/callback with a code, which it trades for tokens.
    /// </summary>
    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        var clientId = BuiltInClientId ?? throw new InvalidOperationException("This build of FluentDL can't sign in to TIDAL. Enter your own client ID and secret instead.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = LoginScopes,
            ["code_challenge"] = CodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };
        var authorize = new Uri(AuthorizeUrl + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LoginTimeout);
        var code = await WaitForCodeAsync(authorize, state, timeout.Token);

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            await RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = RedirectUri,
                ["scope"] = LoginScopes,
            }, basicAuth: null, cancellationToken);
            Access = TidalAccess.Account;
        }
        finally
        {
            tokenLock.Release();
        }
        await ReadAccountAsync(cancellationToken);
        await ReadCountryAsync();
    }

    /// <summary>Forgets the account sign-in. A saved client ID and secret still work afterwards.</summary>
    public async Task LogoutAsync()
    {
        await tokenLock.WaitAsync();
        try
        {
            token = null;
            Access = TidalAccess.None;
            AccountName = null;
            AccountEmail = null;
            accountCountry = null;
            await settings.SaveSettingAsync(SettingKeys.TidalRefreshToken, "");
        }
        finally
        {
            tokenLock.Release();
        }
    }

    /// <summary>GETs a path under openapi.tidal.com/v2 (or a "next" link, which starts with "/"), as JSON:API.</summary>
    internal async Task<JsonApiDocument> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var url = ApiBase + pathAndQuery.TrimStart('/');
        if (!url.Contains("countryCode="))
        {
            url += (url.Contains('?') ? "&" : "?") + "countryCode=" + await ReadCountryAsync();
        }

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            using var response = await http.SendAsync(request, cancellationToken);

            // An expired or revoked token: sign in again once.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                token = null;
                continue;
            }
            // No rate limit showed up in testing (40 requests in 2 s), but TIDAL documents 429 with Retry-After.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt <= 3)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"TIDAL returned {(int)response.StatusCode} for {pathAndQuery}: {ErrorDetail(body)}", null, response.StatusCode);
            }
            return new JsonApiDocument(JsonDocument.Parse(body).RootElement);
        }
    }

    private async Task<string> ReadCountryAsync() =>
        CountryCode = ResolveCountry(await settings.ReadSettingAsync<string>(SettingKeys.TidalCountry), accountCountry ?? RegionInfo.CurrentRegion.TwoLetterISORegionName);

    // The account's username and country, when signed in to an account.
    private async Task ReadAccountAsync(CancellationToken cancellationToken)
    {
        AccountName = null;
        AccountEmail = null;
        accountCountry = null;
        if (Access != TidalAccess.Account)
        {
            return;
        }
        var me = await GetAsync("users/me", cancellationToken);
        if (me.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } user)
        {
            AccountName = JsonApiDocument.Attribute(user, "username") ?? JsonApiDocument.Attribute(user, "email") ?? "TIDAL user";
            AccountEmail = JsonApiDocument.Attribute(user, "email");
            if (string.IsNullOrWhiteSpace(AccountEmail)) AccountEmail = null;
            accountCountry = JsonApiDocument.Attribute(user, "country");
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (token != null && DateTimeOffset.UtcNow < tokenExpires - RenewBefore)
        {
            return token;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (token == null || DateTimeOffset.UtcNow >= tokenExpires - RenewBefore)
            {
                await RequestTokenAsync(cancellationToken);
            }
            return token!;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    // Callers hold tokenLock.
    private async Task RequestTokenAsync(CancellationToken cancellationToken)
    {
        // A sign-in belongs to the app it was made with, so a build without FluentDL's app skips it.
        if (BuiltInClientId != null && await ReadRefreshTokenAsync() is { } refreshToken)
        {
            try
            {
                await RequestTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = BuiltInClientId,
                    ["refresh_token"] = refreshToken,
                    ["scope"] = LoginScopes,
                }, basicAuth: null, cancellationToken);
                Access = TidalAccess.Account;
                return;
            }
            catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                // Signed out elsewhere, or the password changed: forget it, so the next sign-in starts clean.
                await settings.SaveSettingAsync(SettingKeys.TidalRefreshToken, "");
                AccountName = null;
                AccountEmail = null;
                accountCountry = null;
                throw new InvalidOperationException("Your TIDAL sign-in has expired. Sign in again in Settings.", e);
            }
        }

        var clientId = (await settings.ReadSettingAsync<string>(SettingKeys.TidalClientId))?.Trim();
        var clientSecret = (await settings.ReadSettingAsync<string>(SettingKeys.TidalClientSecret))?.Trim();
        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            await RequestClientTokenAsync(clientId, clientSecret, cancellationToken);
            Access = TidalAccess.OwnApp;
            return;
        }

        if (BuiltInClientId == null || BuiltInClientSecret == null)
        {
            Access = TidalAccess.None;
            throw new InvalidOperationException(CanLogin
                ? "Sign in to TIDAL in Settings, or enter your own client ID and secret."
                : "Enter your own TIDAL client ID and secret in Settings.");
        }
        try
        {
            await RequestClientTokenAsync(BuiltInClientId, BuiltInClientSecret, cancellationToken);
            Access = TidalAccess.BuiltIn;
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            Access = TidalAccess.None;
            throw new InvalidOperationException("FluentDL's built-in TIDAL access stopped working. Sign in to TIDAL in Settings.", e);
        }
    }

    // Client credentials: no account, catalog access only.
    private Task RequestClientTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken) =>
        RequestTokenAsync(new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
            basicAuth: Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")), cancellationToken);

    // A value the build put into the assembly (see FluentDL.Sources.csproj), or null.
    private static string? BuildValue(string key) =>
        typeof(TidalApi).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value : null;

    // Callers hold tokenLock. A new refresh token, which TIDAL may send with any answer, replaces the saved one.
    private async Task RequestTokenAsync(Dictionary<string, string> form, string? basicAuth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        if (basicAuth != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        }
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var what = basicAuth != null ? "the client ID and secret" : "the sign-in";
            throw new HttpRequestException($"TIDAL refused {what} ({(int)response.StatusCode}): {ErrorDetail(body)}", null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(body);
        token = json.RootElement.GetProperty("access_token").GetString();
        tokenExpires = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32());
        if (json.RootElement.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { Length: > 0 } newRefreshToken)
        {
            await settings.SaveSettingAsync(SettingKeys.TidalRefreshToken, Protect(newRefreshToken));
        }
    }

    private async Task<string?> ReadRefreshTokenAsync() =>
        await settings.ReadSettingAsync<string>(SettingKeys.TidalRefreshToken) is { Length: > 0 } saved ? Unprotect(saved) : null;

    // Serves the redirect address until the browser comes back to it, then returns the code. Other requests to the
    // port (a favicon) get a 404. The page tells the user to go back to FluentDL.
    private async Task<string> WaitForCodeAsync(Uri authorize, string state, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, loginPort);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            throw new InvalidOperationException($"Port {loginPort} is in use, so the TIDAL sign-in can't come back to FluentDL. Close the program using it and try again.", e);
        }

        try
        {
            await OpenBrowser(authorize);
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(cancellationToken) ?? "";
                var target = requestLine.Split(' ') is [_, var path, ..] ? path : "";
                if (!target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, "404 Not Found", "", cancellationToken);
                    continue;
                }

                var parameters = ParseQuery(target);
                string? error = null;
                if (parameters.GetValueOrDefault("state") != state)
                {
                    error = "The sign-in answer didn't match this sign-in. Try again from FluentDL's Settings.";
                }
                else if (parameters.GetValueOrDefault("error") is { } oauthError)
                {
                    error = $"TIDAL didn't sign you in: {parameters.GetValueOrDefault("error_description") ?? oauthError}";
                }
                else if (parameters.GetValueOrDefault("code") is not { Length: > 0 })
                {
                    error = "TIDAL sent no sign-in code.";
                }

                await RespondAsync(stream, "200 OK", error ?? "You're signed in to TIDAL. You can close this tab and go back to FluentDL.", cancellationToken);
                return error == null ? parameters["code"] : throw new InvalidOperationException(error);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RespondAsync(Stream stream, string status, string message, CancellationToken cancellationToken)
    {
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>FluentDL</title><body style=\"font-family:Segoe UI,sans-serif;margin:3em\"><p>{WebUtility.HtmlEncode(message)}</p></body>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]))
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString((g.First().ElementAtOrDefault(1) ?? "").Replace('+', ' ')));
    }

    // RFC 7636: the challenge is the verifier's SHA-256, base64url-encoded.
    internal static string CodeChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // The refresh token signs in to the account, so it's saved encrypted for the Windows user (DPAPI).
    private static string Protect(string value) =>
        OperatingSystem.IsWindows() ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)) : "";

    private static string? Unprotect(string saved)
    {
        try
        {
            return OperatingSystem.IsWindows() ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved), null, DataProtectionScope.CurrentUser)) : null;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null; // saved by another Windows user, or damaged: sign in again
        }
    }

    // The useful part of an error body: JSON:API "errors[0].detail", OAuth "error_description", or the start of the text.
    private static string ErrorDetail(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? "";
            }
            if (root.TryGetProperty("error_description", out var description))
            {
                return description.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }
        return body.Length > 200 ? body[..200] : body;
    }

    // The setting when it is a two-letter code, else the account's or Windows' region, else US (Windows' "World"
    // region is "001").
    internal static string ResolveCountry(string? setting, string fallbackRegion)
    {
        static bool IsCountry(string? code) => code is { Length: 2 } && code.All(char.IsAsciiLetter);
        return IsCountry(setting?.Trim()) ? setting!.Trim().ToUpperInvariant()
            : IsCountry(fallbackRegion) ? fallbackRegion.ToUpperInvariant()
            : "US";
    }
}
