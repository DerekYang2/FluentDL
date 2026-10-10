using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;

namespace FluentDL.Services.Tidal;

/// <summary>
/// Requests to TIDAL's official API (openapi.tidal.com/v2). It signs in with the client id and secret from Settings
/// (the client-credentials flow, no TIDAL account needed), keeps the token, which lasts 4 hours, and gets a new one
/// shortly before it expires. Every request carries a country (the TIDAL country setting, else the Windows region),
/// which decides availability and ranking.
/// </summary>
public sealed class TidalApi
{
    private const string ApiBase = "https://openapi.tidal.com/v2/";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    private readonly HttpClient http;
    private readonly ILocalSettingsService settings;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;

    public TidalApi(ILocalSettingsService settings)
        : this(settings, new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }))
    {
    }

    internal TidalApi(ILocalSettingsService settings, HttpClient http)
    {
        this.settings = settings;
        this.http = http;
        CountryCode = ResolveCountry(null, RegionInfo.CurrentRegion.TwoLetterISORegionName);
    }

    /// <summary>
    /// The country of the last request. It changes what TIDAL offers and how it ranks results (Canada ranks another
    /// Random Access Memories edition second than the US).
    /// </summary>
    public string CountryCode { get; private set; }

    public bool IsSignedIn => token != null;

    /// <summary>Gets a fresh token with the saved client id and secret. Throws with the reason when that fails.</summary>
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
    }

    /// <summary>GETs a path under openapi.tidal.com/v2 (or a "next" link, which starts with "/"), as JSON:API.</summary>
    internal async Task<JsonApiDocument> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var url = ApiBase + pathAndQuery.TrimStart('/');
        if (!url.Contains("countryCode="))
        {
            CountryCode = ResolveCountry(await settings.ReadSettingAsync<string>(SettingKeys.TidalCountry), RegionInfo.CurrentRegion.TwoLetterISORegionName);
            url += (url.Contains('?') ? "&" : "?") + "countryCode=" + CountryCode;
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
        var clientId = (await settings.ReadSettingAsync<string>(SettingKeys.TidalClientId))?.Trim();
        var clientSecret = (await settings.ReadSettingAsync<string>(SettingKeys.TidalClientSecret))?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Enter a TIDAL client ID and secret in Settings.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"TIDAL refused the client ID and secret ({(int)response.StatusCode}): {ErrorDetail(body)}", null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(body);
        token = json.RootElement.GetProperty("access_token").GetString();
        tokenExpires = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32());
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

    // The setting when it is a two-letter code, else the Windows region, else US (Windows' "World" region is "001").
    internal static string ResolveCountry(string? setting, string windowsRegion)
    {
        static bool IsCountry(string? code) => code is { Length: 2 } && code.All(char.IsAsciiLetter);
        return IsCountry(setting?.Trim()) ? setting!.Trim().ToUpperInvariant()
            : IsCountry(windowsRegion) ? windowsRegion.ToUpperInvariant()
            : "US";
    }
}
