using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FluentDL.Services.CustomSpotify
{
    public interface ISpotifyISRCService
    {
        Task<string> GetIsrc(string spotifyId, CancellationToken cancellationToken = default);
        Task<string> GetIsrcViaSoundplateAsync(string spotifyId, CancellationToken cancellationToken = default);
    }

    public class SpotifyISRCService : ISpotifyISRCService
    {
        private readonly HttpClient _httpClient;
        private readonly IClientTokenService _tokenService;
        private readonly ILogger<SpotifyISRCService> _logger;

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36";
        private const string SpotifyMetadataTrackUrlTemplate = "https://spclient.wg.spotify.com/metadata/4/track/{0}?market=from_token";
        private const string SpotifyBase62Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private const string SoundplateSpotifyApiUrl = "https://phpstack-822472-6184058.cloudwaysapps.com/api/spotify.php";
        private const string SoundplateRefererUrl = "https://phpstack-822472-6184058.cloudwaysapps.com/?";
        private static readonly Regex IsrcRegex = new(@"\b[A-Z]{2}[A-Z0-9]{3}\d{7}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SpotifyISRCService(HttpClient httpClient, IClientTokenService tokenService, ILogger<SpotifyISRCService> logger)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<string> GetIsrc(string spotifyId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (string.IsNullOrWhiteSpace(spotifyId))
                {
                    throw new ArgumentException("Spotify id cannot be empty.", nameof(spotifyId));
                }

                var normalizedTrackId = ExtractSpotifyTrackId(spotifyId);

                try
                {
                    return await GetIsrcViaSpotifyMetadataAsync(normalizedTrackId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Spotify metadata ISRC lookup failed for track {TrackId}. Falling back to third party.", normalizedTrackId);
                    return await GetIsrcViaSoundplateAsync(normalizedTrackId, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Retrieves a Spotify track ISRC using Spotify metadata endpoint.
        /// </summary>
        private async Task<string> GetIsrcViaSpotifyMetadataAsync(string spotifyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(spotifyId))
            {
                throw new ArgumentException("Spotify id cannot be empty.", nameof(spotifyId));
            }

            var normalizedTrackId = ExtractSpotifyTrackId(spotifyId);
            var trackGid = SpotifyEntityIdToGid(normalizedTrackId);
            var metadataUrl = string.Format(SpotifyMetadataTrackUrlTemplate, trackGid);
            var (anonymousToken, _) = await _tokenService.GetAnonymousTokenAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonymousToken);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Spotify metadata ISRC returned status {(int)response.StatusCode} ({body})");
            }

            var isrc = ExtractSpotifyMetadataIsrc(body);
            if (string.IsNullOrWhiteSpace(isrc))
            {
                throw new InvalidOperationException("ISRC missing in Spotify metadata response.");
            }

            return isrc;
        }

        /// <summary>
        /// Retrieves a Spotify track ISRC using the Soundplate endpoint.
        /// </summary>
        public async Task<string> GetIsrcViaSoundplateAsync(string spotifyId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(spotifyId))
            {
                throw new ArgumentException("Spotify id cannot be empty.", nameof(spotifyId));
            }

            var normalizedTrackId = ExtractSpotifyTrackId(spotifyId);
            var spotifyTrackUrl = $"https://open.spotify.com/track/{normalizedTrackId}";
            var encodedTrackUrl = Uri.EscapeDataString(spotifyTrackUrl);
            var requestUrl = $"{SoundplateSpotifyApiUrl}?q={encodedTrackUrl}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("Accept", "*/*");
            request.Headers.Add("Referer", SoundplateRefererUrl);
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9,id;q=0.8");
            request.Headers.Add("Sec-CH-UA", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
            request.Headers.Add("Sec-CH-UA-Mobile", "?0");
            request.Headers.Add("Sec-CH-UA-Platform", "\"Windows\"");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Priority", "u=1, i");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Soundplate ISRC returned status {(int)response.StatusCode} ({body})");
            }

            var isrc = FirstIsrcMatch(body);

            if (string.IsNullOrWhiteSpace(isrc))
            {
                throw new InvalidOperationException("ISRC missing in Soundplate response.");
            }

            return isrc;
        }

        private static string FirstIsrcMatch(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var match = IsrcRegex.Match(input);
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
        }

        private static string ExtractSpotifyMetadataIsrc(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            SpotifyMetadataResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<SpotifyMetadataResponse>(payload);
            }
            catch (JsonException)
            {
                return FirstIsrcMatch(payload);
            }

            if (response?.ExternalIds is not null)
            {
                foreach (var externalId in response.ExternalIds)
                {
                    if (externalId is null || !string.Equals(externalId.Type?.Trim(), "isrc", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var isrc = FirstIsrcMatch(externalId.Id);
                    if (!string.IsNullOrWhiteSpace(isrc))
                    {
                        return isrc;
                    }
                }
            }

            return FirstIsrcMatch(payload);
        }

        private static string SpotifyEntityIdToGid(string entityId)
        {
            var normalizedEntityId = entityId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedEntityId))
            {
                throw new ArgumentException("Spotify id cannot be empty.", nameof(entityId));
            }

            var value = BigInteger.Zero;
            var radix = new BigInteger(62);

            foreach (var symbol in normalizedEntityId)
            {
                var index = SpotifyBase62Alphabet.IndexOf(symbol);
                if (index < 0)
                {
                    throw new ArgumentException($"Spotify id contains invalid base62 character '{symbol}'.", nameof(entityId));
                }

                value = (value * radix) + index;
            }

            var gidBytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (gidBytes.Length > 16)
            {
                throw new InvalidOperationException("Spotify track id conversion produced an out-of-range GID.");
            }

            return Convert.ToHexString(gidBytes).ToLowerInvariant().PadLeft(32, '0');
        }

        private static string ExtractSpotifyTrackId(string input)
        {
            var value = input.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Spotify id cannot be empty.", nameof(input));
            }

            const string uriPrefix = "spotify:track:";
            if (value.StartsWith(uriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[uriPrefix.Length..];
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return value;
            }

            if (!uri.Host.Contains("spotify.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Provided URL is not a Spotify URL.", nameof(input));
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0].Equals("track", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1];
            }

            throw new ArgumentException("Unable to extract Spotify track id from input.", nameof(input));
        }

        private sealed class SpotifyMetadataResponse
        {
            [JsonPropertyName("external_id")]
            public List<SpotifyExternalId>? ExternalIds { get; set; }
        }

        private sealed class SpotifyExternalId
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("id")]
            public string? Id { get; set; }
        }
    }
}
