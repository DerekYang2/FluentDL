using Newtonsoft.Json;
using QobuzApiSharp.Models.Content;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using FluentDL.Helpers;
using System.Net;

namespace FluentDL.Services.Sources
{
    internal sealed partial class QobuzSource
    {
        private bool HttpClientInitialized = false;
        internal ProviderRequestPolicy CatalogRequests { get; set; } = ProviderRequests.Qobuz;
        public const string BaseUrl = "https://www.qobuz.com/api.json/0.2";
        public enum EnumQobuzSearchType
        {
            None,
            ByReleaseName,
            ByMainArtist,
            ByLabel,
        }

        public void InitializeQobuzHttpClient(string appId, string? userAuthToken = "")
        {
            HttpClientInitialized = false;
            try
            {
                QobuzHttpClient = new HttpClient();
                QobuzHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0");
                QobuzHttpClient.DefaultRequestHeaders.Add("X-App-Id", appId);
                if (!string.IsNullOrWhiteSpace(userAuthToken))
                {
                    QobuzHttpClient.DefaultRequestHeaders.Add("X-User-Auth-Token", userAuthToken);
                }
                HttpClientInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to initialize qobuz http client: " + ex.ToString());
            }
        }

        public async Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
        {
            if (!HttpClientInitialized)
                return null;

            // /playlist/get?playlist_id=2049430&extra=track_ids
            var requestUrl = $"{BaseUrl}/playlist/get?playlist_id={playlistId}&extra=track_ids";

            using var response = await CatalogRequests.SendAsync(QobuzHttpClient, () => new(HttpMethod.Get, requestUrl), cancellationToken);
            if (response?.IsSuccessStatusCode ?? false)
            {
                var jsonResultString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(jsonResultString))
                    return null;

                return JsonConvert.DeserializeObject<Playlist>(jsonResultString);
            }
            return null;
        }

        public async IAsyncEnumerable<T> ApiSearch<T>(string query, int offset = 0, int limit = 25, EnumQobuzSearchType searchType = EnumQobuzSearchType.None,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!HttpClientInitialized)
                yield break;

            // Check if generic type is Track or Album
            bool isTrack = typeof(T) == typeof(Track);

            const int searchChunkSize = 28;  //  Used by web player
            limit += offset;  // Adjust limit to include offset

            // Example: https://www.qobuz.com/api.json/0.2/track/search?query=test%20%23ByLabel&offset=28&limit=28
            string searchTypeString = searchType switch
            {
                EnumQobuzSearchType.ByReleaseName => "ByReleaseName",
                EnumQobuzSearchType.ByMainArtist => "ByMainArtist",
                EnumQobuzSearchType.ByLabel => "ByLabel",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(searchTypeString))
            {
                searchTypeString = "%20%23" + searchTypeString;
            }

            while (offset < limit)
            {
                int currentLimit = Math.Min(searchChunkSize, limit - offset);
                var requestUrl = $"{BaseUrl}/{(isTrack ? "track" : "album")}/search?query={Uri.EscapeDataString(query)}{searchTypeString}&offset={offset}&limit={currentLimit}";

                using var response = await CatalogRequests.SendAsync(QobuzHttpClient, () => new(HttpMethod.Get, requestUrl), cancellationToken);
                response.EnsureSuccessStatusCode();

                if (response?.IsSuccessStatusCode ?? false)
                {
                    var jsonResultString = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(jsonResultString))
                        yield break;

                    // Create JSON object from the response
                    Newtonsoft.Json.Linq.JObject jsonObject = await Task.Run(() =>
                    {
                        try
                        {
                            return Newtonsoft.Json.Linq.JObject.Parse(jsonResultString);
                        }
                        catch (JsonReaderException)
                        {
                            // Handle JSON parsing error
                            return [];
                        }
                    });

                    var jsonArray = jsonObject[isTrack? "tracks" : "albums"]?["items"];

                    if (jsonArray != null)
                    {
                        foreach (var item in jsonArray)
                        {
                            // Deserialize each item to Track/Album object
                            T? qobuzObj = item.ToObject<T>();
                            if (qobuzObj != null)
                            {
                                yield return qobuzObj;
                            }
                        }
                    }
                }
                else
                {
                    yield break;
                }

                offset += currentLimit;
            }
        }

        // Qobuz has no ISRC lookup, only a text search, so a result counts only when its ISRC is the one asked for
        // (bug 8: the top result was returned unchecked, and the HTTP client was used before sign-in).
        public async Task<Track?> SearchISRC(string isrc, CancellationToken cancellationToken = default)
        {
            if (!HttpClientInitialized)
            {
                return null;
            }

            try
            {
                var requestUrl = $"{BaseUrl}/track/search?query={Uri.EscapeDataString(isrc)}&limit=10";

                using var response = await CatalogRequests.SendAsync(QobuzHttpClient, () => new(HttpMethod.Get, requestUrl), cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response?.IsSuccessStatusCode ?? false) {
                
                    var jsonResultString = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(jsonResultString))
                        return null;
                    // Create JSON object from the response
                    Newtonsoft.Json.Linq.JObject jsonObject = await Task.Run(() =>
                    {
                        try
                        {
                            return Newtonsoft.Json.Linq.JObject.Parse(jsonResultString);
                        }
                        catch (JsonReaderException)
                        {
                            // Handle JSON parsing error
                            return [];
                        }
                    });
                    var jsonArray = jsonObject["tracks"]?["items"];
                    if (jsonArray != null)
                    {
                        return jsonArray.Select(item => item.ToObject<Track>())
                            .FirstOrDefault(track => IsAvailableConversionTrack(track)
                                && string.Equals(track!.Isrc, isrc, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine("Error searching ISRC: " + ex.ToString());
            }
            return null;
        }

        private async Task<T?> GetCatalogAsync<T>(string endpoint, CancellationToken token = default)
        {
            if (!HttpClientInitialized) return default;
            using var response = await CatalogRequests.SendAsync(QobuzHttpClient,
                () => new(HttpMethod.Get, BaseUrl + "/" + endpoint.TrimStart('/')), token);
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            response.EnsureSuccessStatusCode();
            return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(token));
        }
    }
}
