using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
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
        private readonly ILogger<SpotifyISRCService> _logger;

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36";

        private const string Endpoint = "https://sp.afkarxyz.qzz.io/api";
        private const string SoundplateSpotifyApiUrl = "https://phpstack-822472-6184058.cloudwaysapps.com/api/spotify.php";
        private const string SoundplateRefererUrl = "https://phpstack-822472-6184058.cloudwaysapps.com/?";
        private static readonly Regex IsrcRegex = new(@"\b[A-Z]{2}[A-Z0-9]{3}\d{7}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SpotifyISRCService(HttpClient httpClient, ILogger<SpotifyISRCService> logger)
        {
            _httpClient = httpClient;
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
                    return await GetIsrcSpotiFlac(normalizedTrackId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Primary ISRC lookup failed for track {TrackId}. Falling back to Soundplate.", normalizedTrackId);
                    return await GetIsrcViaSoundplateAsync(normalizedTrackId, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
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

        private async Task<string> GetIsrcSpotiFlac(string spotifyTrackId, CancellationToken cancellationToken)
        {
            var requestUrl = $"{Endpoint.TrimEnd('/')}/isrc/{spotifyTrackId}";
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                getRequest.Headers.Add("User-Agent", UserAgent);
                getRequest.Headers.Add("Accept", "application/json");

            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
            var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"isrcfinder returned status {(int)getResponse.StatusCode}");
            }

            if (!getBody.TryGetProperty("isrc", out var isrcProperty))
            {
                throw new InvalidOperationException("ISRC not found in isrcfinder response.");
            }

            var isrc = isrcProperty.GetString();
            if (string.IsNullOrWhiteSpace(isrc))
            {
                throw new InvalidOperationException("ISRC not found in isrcfinder response.");
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
    }
}
