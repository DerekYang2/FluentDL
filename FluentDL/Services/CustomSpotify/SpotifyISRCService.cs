using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FluentDL.Services.CustomSpotify
{
    public interface ISpotifyISRCService
    {
        Task<string> LookupIsrcViaIsrcFinderAsync(string spotifyId, CancellationToken cancellationToken = default);
    }

    public class SpotifyISRCService : ISpotifyISRCService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SpotifyISRCService> _logger;

        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36 Edg/146.0.0.0";
        private const string IsrcFinderEndpoint = "https://www.isrcfinder.com/";
        private static readonly Regex CsrfTokenPattern = new(@"name=[\""']csrfmiddlewaretoken[\""'][^>]*value=[\""']([^\""']+)[\""']", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IsrcPattern = new("\\b[A-Z]{2}[A-Z0-9]{3}\\d{7}\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly SemaphoreSlim _phpStackSemaphore = new SemaphoreSlim(1, 1);
        public SpotifyISRCService(HttpClient httpClient, ILogger<SpotifyISRCService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> LookupIsrcViaIsrcFinderAsync(string spotifyId, CancellationToken cancellationToken = default)
        {
            await _phpStackSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (string.IsNullOrWhiteSpace(spotifyId))
                {
                    throw new ArgumentException("Spotify id cannot be empty.", nameof(spotifyId));
                }

                var spotifyUrl = $"https://open.spotify.com/track/{spotifyId.Trim()}";

                var cookieContainer = new CookieContainer();
                using var handler = new HttpClientHandler { CookieContainer = cookieContainer };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

                using var getRequest = new HttpRequestMessage(HttpMethod.Get, IsrcFinderEndpoint);
                getRequest.Headers.Add("User-Agent", UserAgent);
                getRequest.Headers.Add("Referer", IsrcFinderEndpoint);
                getRequest.Headers.Add("Origin", "https://www.isrcfinder.com");

                using var getResponse = await client.SendAsync(getRequest, cancellationToken);
                var getBody = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!getResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"isrcfinder returned status {(int)getResponse.StatusCode}");
                }

                var token = ExtractCsrfToken(getBody);
                if (string.IsNullOrWhiteSpace(token))
                {
                    var uri = new Uri(IsrcFinderEndpoint);
                    token = cookieContainer.GetCookies(uri)
                        .Cast<Cookie>()
                        .FirstOrDefault(c => string.Equals(c.Name, "csrftoken", StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("csrf token not found");
                }

                var form = new Dictionary<string, string>
                {
                    ["csrfmiddlewaretoken"] = token,
                    ["URI"] = spotifyUrl
                };

                using var postRequest = new HttpRequestMessage(HttpMethod.Post, IsrcFinderEndpoint)
                {
                    Content = new FormUrlEncodedContent(form)
                };
                postRequest.Headers.Add("User-Agent", UserAgent);
                postRequest.Headers.Add("Referer", IsrcFinderEndpoint);
                postRequest.Headers.Add("Origin", "https://www.isrcfinder.com");

                using var postResponse = await client.SendAsync(postRequest, cancellationToken);
                var postBody = await postResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!postResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"isrcfinder POST returned status {(int)postResponse.StatusCode}");
                }

                var isrc = FirstIsrcMatch(postBody);
                if (string.IsNullOrWhiteSpace(isrc))
                {
                    throw new InvalidOperationException("ISRC not found in isrcfinder response");
                }

                return isrc;
            } finally
            {
                _phpStackSemaphore.Release();
            }
        }

        private static string ExtractCsrfToken(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            var match = CsrfTokenPattern.Match(body);
            if (!match.Success || match.Groups.Count < 2)
            {
                return string.Empty;
            }

            return match.Groups[1].Value.Trim();
        }

        private static string FirstIsrcMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = IsrcPattern.Match(text);
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
        }

        private sealed class PhpStackIsrcResponse
        {
            [JsonPropertyName("isrc")]
            public string? Isrc { get; set; }
        }
    }
}
