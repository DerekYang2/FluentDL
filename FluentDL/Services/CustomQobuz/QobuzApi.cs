using Newtonsoft.Json;
using QobuzApiSharp.Models.Content;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TagLib.Id3v2;

namespace FluentDL.Services
{
    internal partial class QobuzApi
    {
        private static bool HttpClientInitialized = false;
        public const string BaseUrl = "https://www.qobuz.com/api.json/0.2";
        public enum EnumQobuzSearchType
        {
            None,
            ByReleaseName,
            ByMainArtist,
            ByLabel,
        }

        public static void InitializeQobuzHttpClient(string appId, string? userAuthToken = "")
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

        public static async Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
        {
            if (!HttpClientInitialized)
                return null;

            // /playlist/get?playlist_id=2049430&extra=track_ids
            var requestUrl = $"{BaseUrl}/playlist/get?playlist_id={playlistId}&extra=track_ids";

            using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
            var response = await QobuzHttpClient.SendAsync(request, cancellationToken);
            if (response?.IsSuccessStatusCode ?? false)
            {
                var jsonResultString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(jsonResultString))
                    return null;

                return JsonConvert.DeserializeObject<Playlist>(jsonResultString);
            }
            return null;
        }

        public static async IAsyncEnumerable<T> ApiSearch<T>(string query, int limit = 25, EnumQobuzSearchType searchType = EnumQobuzSearchType.None)
        {
            if (!HttpClientInitialized)
                yield break;

            // Check if generic type is Track or Album
            bool isTrack = typeof(T) == typeof(Track);

            const int searchChunkSize = 28;  //  Used by web player
            int offset = 0;

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

                using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
                var response = await QobuzHttpClient.SendAsync(request);

                if (response?.IsSuccessStatusCode ?? false)
                {
                    var jsonResultString = await response.Content.ReadAsStringAsync();
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
    }
}
