using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentDL.Services.CustomSpotify
{
    public interface IClientTokenService
    {
        Task<(string token, DateTimeOffset expiry)> GetClientTokenAsync(CancellationToken cancellationToken = default);

        // Added the new interface method
        Task<(string token, DateTimeOffset expiry)> GetAnonymousTokenAsync(CancellationToken cancellationToken = default);
    }

    public class ClientTokenService : IClientTokenService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ClientTokenService> _logger;
        private readonly IMemoryCache _cache;

        private string _deviceId = Guid.NewGuid().ToString(); // Persist a device ID for fallbacks

        // Cached TOTP state
        private byte[]? _totpSecret = null;
        private int _totpVersion = 0;
        private readonly SemaphoreSlim _totpSemaphore = new SemaphoreSlim(1, 1);

        private const string WEB_CLIENT_ID = "d8a5ed958d274c2e8ee717e6a4b0971d";
        private const string CLIENT_VERSION = "1.2.82.42.g4c44af04";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private const string ClientTokenCacheKey = "UpstreamClientToken";
        private const string AnonymousTokenCacheKey = "AnonymousAccessToken";

        public ClientTokenService(HttpClient httpClient, ILogger<ClientTokenService> logger, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
        }

        public async Task<(string token, DateTimeOffset expiry)> GetClientTokenAsync(CancellationToken cancellationToken = default)
        {
            // Try cache first
            if (_cache.TryGetValue<(string token, DateTimeOffset expiry)>(ClientTokenCacheKey, out var cached) &&
                cached.expiry > DateTimeOffset.UtcNow.AddSeconds(10))
            {
                _logger.LogDebug("Returning cached client token");
                return cached;
            }

            var ClientTokenEndpoint = "https://clienttoken.spotify.com/v1/clienttoken";
            var payload = JsonSerializer.Serialize(
                new
                {
                    client_data = new
                    {
                        client_version = CLIENT_VERSION,
                        client_id = WEB_CLIENT_ID,
                        js_sdk_data = new
                        {
                            device_brand = "unknown",
                            device_model = "unknown",
                            os = "windows",
                            os_version = "NT 10.0",
                            device_id = _deviceId,
                            device_type = "computer",
                        },
                    },
                }
            );

            using var request = new HttpRequestMessage(HttpMethod.Post, ClientTokenEndpoint);
            request.Content = new StringContent(payload, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("Authority", "clienttoken.spotify.com");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            try
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Token endpoint returned {Status}. Response: {Response}", response.StatusCode, Truncate(responseText));
                    throw new HttpRequestException($"Upstream returned {response.StatusCode}");
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var tokenResponse = JsonSerializer.Deserialize<ClientTokenResponse>(responseText, options);

                if (tokenResponse?.granted_token?.token == null)
                {
                    _logger.LogError("Token response missing granted_token. Raw: {Response}", Truncate(responseText));
                    throw new InvalidOperationException("Token response missing granted_token");
                }

                var expiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.granted_token.expires_after_seconds);
                var result = (tokenResponse.granted_token.token, expiry);

                var cacheDuration = expiry - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10);
                if (cacheDuration > TimeSpan.Zero)
                {
                    _cache.Set(ClientTokenCacheKey, result, cacheDuration);
                }

                _logger.LogInformation("Obtained client token, expires at {Expiry}", expiry);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Client token request cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting client token");
                throw;
            }
        }

        public async Task<(string token, DateTimeOffset expiry)> GetAnonymousTokenAsync(CancellationToken cancellationToken = default)
        {
            // Step 1: Check cache
            if (_cache.TryGetValue<(string token, DateTimeOffset expiry)>(AnonymousTokenCacheKey, out var cached) &&
                cached.expiry > DateTimeOffset.UtcNow.AddSeconds(10))
            {
                _logger.LogDebug("Returning cached anonymous token");
                return cached;
            }

            // Step 2: Fetch the TOTP secret if not already loaded
            await EnsureTotpSecretAsync(cancellationToken);

            long serverTimeMs = 0;
            string spTCookie = string.Empty;

            // Step 3: Fetch the main page to get serverTime and cookies
            using var pageRequest = new HttpRequestMessage(HttpMethod.Get, "https://open.spotify.com/");
            pageRequest.Headers.Add("User-Agent", UserAgent);
            pageRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            try
            {
                using var pageResponse = await _httpClient.SendAsync(pageRequest, cancellationToken);

                if (pageResponse.IsSuccessStatusCode)
                {
                    string html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);

                    // Extract serverTime from the embedded config
                    var configMatch = Regex.Match(html, @"id=""appServerConfig"">([^<]+)</script>");
                    if (configMatch.Success)
                    {
                        try
                        {
                            string base64Config = configMatch.Groups[1].Value;
                            string configJson = Encoding.UTF8.GetString(Convert.FromBase64String(base64Config));
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var config = JsonSerializer.Deserialize<SpotifyServerConfig>(configJson, options);

                            if (config != null)
                            {
                                serverTimeMs = config.serverTime * 1000;
                                _logger.LogDebug("Got serverTime: {ServerTime} (ms: {ServerTimeMs})", config.serverTime, serverTimeMs);
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Failed to parse server config");
                        }
                    }

                    // Get sp_t cookie from response headers
                    if (pageResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
                    {
                        foreach (var cookieHeader in setCookies)
                        {
                            var match = Regex.Match(cookieHeader, @"sp_t=([^;]+)");
                            if (match.Success)
                            {
                                spTCookie = match.Groups[1].Value;
                                _logger.LogDebug("Got sp_t cookie: {Cookie}", spTCookie);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogError("Failed to fetch Spotify page: {StatusCode}", pageResponse.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while fetching main page for anonymous token");
            }

            if (serverTimeMs == 0) serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.IsNullOrEmpty(spTCookie)) spTCookie = _deviceId;

            // Step 4: Generate TOTP
            string totp = GenerateSpotifyTotp(serverTimeMs);
            _logger.LogDebug("Generated TOTP: {Totp} (version: {Version})", totp, _totpVersion);

            // Step 5: Get access token
            string tokenUrl = $"https://open.spotify.com/api/token?reason=init&productType=web-player&totp={totp}&totpServer={totp}&totpVer={_totpVersion}";
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, tokenUrl);
            tokenRequest.Headers.Add("Accept", "*/*");
            tokenRequest.Headers.Add("User-Agent", UserAgent);
            tokenRequest.Headers.Add("Referer", "https://open.spotify.com/");
            tokenRequest.Headers.Add("Cookie", $"sp_t={spTCookie}");

            using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
            var tokenResponseText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get anonymous access token. Status: {Status}, Response: {Response}", tokenResponse.StatusCode, Truncate(tokenResponseText));
                throw new HttpRequestException("Failed to fetch anonymous token.");
            }

            var tokenOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var anonData = JsonSerializer.Deserialize<AnonymousTokenResponse>(tokenResponseText, tokenOptions);

            if (string.IsNullOrEmpty(anonData?.accessToken))
            {
                throw new InvalidOperationException("Anonymous token response missing accessToken.");
            }

            var expiryOffset = DateTimeOffset.FromUnixTimeMilliseconds(anonData.accessTokenExpirationTimestampMs);
            var cacheResult = (anonData.accessToken, expiryOffset);

            var cacheTime = expiryOffset - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10);
            if (cacheTime > TimeSpan.Zero)
            {
                _cache.Set(AnonymousTokenCacheKey, cacheResult, cacheTime);
            }

            _logger.LogInformation("Anonymous access token obtained successfully");
            return cacheResult;
        }

        private async Task EnsureTotpSecretAsync(CancellationToken cancellationToken)
        {
            if (_totpSecret != null) return;

            // Ensure only one thread fetches the secret
            await _totpSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_totpSecret != null) return;

                string url = "https://raw.githubusercontent.com/xyloflake/spot-secrets-go/refs/heads/main/secrets/secretDict.json";
                try
                {
                    string json = await _httpClient.GetStringAsync(url, cancellationToken);
                    var secretDict = JsonSerializer.Deserialize<Dictionary<string, int[]>>(json);

                    if (secretDict != null && secretDict.Count > 0)
                    {
                        int maxVersion = 0;
                        int[]? cipherArray = null;

                        foreach (var kvp in secretDict)
                        {
                            if (int.TryParse(kvp.Key, out int version) && version > maxVersion)
                            {
                                maxVersion = version;
                                cipherArray = kvp.Value;
                            }
                        }

                        if (cipherArray != null)
                        {
                            _totpVersion = maxVersion;
                            var sb = new StringBuilder();
                            for (int i = 0; i < cipherArray.Length; i++)
                            {
                                int transformed = cipherArray[i] ^ ((i % 33) + 9);
                                sb.Append(transformed.ToString());
                            }
                            _totpSecret = Encoding.UTF8.GetBytes(sb.ToString());
                            _logger.LogInformation("TOTP secret loaded, version: {Version}", _totpVersion);
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to fetch/parse TOTP secret from GitHub");
                }

                // Fallback to hardcoded version 61 if fetch failed
                _logger.LogWarning("Using fallback TOTP secret (version 61)");
                int[] fallbackCipher = new int[] { 44, 55, 47, 42, 70, 40, 34, 114, 76, 74, 50, 111, 120, 97, 75, 76, 94, 102, 43, 69, 49, 120, 118, 80, 64, 78 };
                _totpVersion = 61;
                var fallbackSb = new StringBuilder();
                for (int i = 0; i < fallbackCipher.Length; i++)
                {
                    int transformed = fallbackCipher[i] ^ ((i % 33) + 9);
                    fallbackSb.Append(transformed.ToString());
                }
                _totpSecret = Encoding.UTF8.GetBytes(fallbackSb.ToString());
            }
            finally
            {
                _totpSemaphore.Release();
            }
        }

        private string GenerateSpotifyTotp(long serverTimeMs)
        {
            if (_totpSecret == null) return "000000";

            // Standard HMAC-SHA1 TOTP (RFC 6238)
            long counter = serverTimeMs / 1000 / 30;

            // Convert counter to 8-byte big-endian
            byte[] counterBytes = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                counterBytes[i] = (byte)(counter & 0xFF);
                counter >>= 8;
            }

            using var hmac = new HMACSHA1(_totpSecret);
            byte[] hash = hmac.ComputeHash(counterBytes);

            // Dynamic truncation
            int offset = hash[hash.Length - 1] & 0x0F;
            int binary = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

            int otp = binary % 1000000;
            return otp.ToString("D6");
        }

        private static string Truncate(string s, int max = 200) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "...");

        // --- Inner Models ---
        private class ClientTokenResponse
        {
            public GrantedToken? granted_token { get; set; }
        }

        private class GrantedToken
        {
            public string? token { get; set; }
            public int expires_after_seconds { get; set; }
        }

        private class SpotifyServerConfig
        {
            public long serverTime { get; set; }
        }

        private class AnonymousTokenResponse
        {
            public string? accessToken { get; set; }
            public long accessTokenExpirationTimestampMs { get; set; }
        }
    }
}