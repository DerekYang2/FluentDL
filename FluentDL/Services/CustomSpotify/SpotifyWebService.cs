using FluentDL.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FluentDL.Services.CustomSpotify
{
    public interface ISpotifyWebService
    {
        IAsyncEnumerable<SongSearchObject> SearchAnonymousAsync(string searchTerm, int limit = 20, CancellationToken cancellationToken = default);
        IAsyncEnumerable<SongSearchObject> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default);
        Task<SongSearchObject?> GetTrack(string trackId, CancellationToken cancellationToken = default);
        Task<bool> IsAuthenticated(int timeoutMs = 5000, CancellationToken cancellationToken = default);
    }

    public class SpotifyWebService : ISpotifyWebService
    {
        private readonly HttpClient _httpClient;
        private readonly IClientTokenService _tokenService;
        private readonly ILogger<SpotifyWebService> _logger;

        private const string SearchEndpoint = "https://api-partner.spotify.com/pathfinder/v2/query";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private const string CLIENT_VERSION = "1.2.82.42.g4c44af04";

        private const string SearchTracksPayloadTemplate = """
        {
          "variables": {
            "searchTerm": "$0",
            "offset": $1,
            "limit": $2,
            "numberOfTopResults": $2,
            "includeAudiobooks": false,
            "includePreReleases": false
          },
          "operationName": "searchTracks",
          "extensions": {
            "persistedQuery": {
              "version": 1,
              "sha256Hash": "59ee4a659c32e9ad894a71308207594a65ba67bb6b632b183abe97303a51fa55"
            }
          }
        }
        """;

        private const string FetchPlaylistContentsPayloadTemplate = """
        {
          "variables": {
            "uri": "spotify:playlist:$0",
            "offset": $1,
            "limit": $2
          },
          "operationName": "fetchPlaylistContents",
          "extensions": {
            "persistedQuery": {
              "version": 1,
              "sha256Hash": "30d415ed189d2699051b60bd0b17ea06467a01bc26d44e8058975e37e9f5fbf6"
            }
          }
        }
        """;

        private const string GetTrackPayload = """
        {
            "variables": {
                "uri": "spotify:track:$0"
            },
            "operationName": "getTrack",
            "extensions": {
                "persistedQuery": {
                    "version": 1,
                    "sha256Hash": "612585ae06ba435ad26369870deaae23b5c8800a256cd8a57e08eddc25a37294"
                }
            }
        }
        """;

        public SpotifyWebService(HttpClient httpClient, IClientTokenService tokenService, ILogger<SpotifyWebService> logger)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<bool> IsAuthenticated(int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            try
            {
                // Link an external cancellation token (if caller provided one) with the timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);
                var token = cts.Token;

                // Start both requests with the same cancellation token
                var clientTokenTask = _tokenService.GetClientTokenAsync(token);
                var anonTokenTask = _tokenService.GetAnonymousTokenAsync(token);

                // Wait for both to complete or throw (including OperationCanceledException)
                await Task.WhenAll(clientTokenTask, anonTokenTask).ConfigureAwait(false);

                // Both tasks completed successfully — get results
                var (clientToken, clientExpiry) = await clientTokenTask.ConfigureAwait(false);
                var (anonToken, anonExpiry) = await anonTokenTask.ConfigureAwait(false);

                // Validate tokens and expiry
                if (clientToken is null || anonToken is null) return false;

                var now = DateTime.UtcNow;
                return clientExpiry > now && anonExpiry > now;
            }
            catch (OperationCanceledException)
            {
                // Timeout or external cancellation
                return false;
            }
            catch (Exception)
            {
                // Log the exception if you have logging, then return false
                return false;
            }
        }

        public async Task<string> QuerySpotify(string jsonBody, CancellationToken cancellationToken = default)
        {
            var clientTokenTask = _tokenService.GetClientTokenAsync(cancellationToken);
            var anonTokenTask = _tokenService.GetAnonymousTokenAsync(cancellationToken);
            await Task.WhenAll(clientTokenTask, anonTokenTask);
            var clientToken = (await clientTokenTask).token;
            var anonToken = (await anonTokenTask).token;

            using var request = new HttpRequestMessage(HttpMethod.Post, SearchEndpoint);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            // Set up the specific headers required by Spotify
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("Accept-Language", "en");
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("app-platform", "WebPlayer");
            request.Headers.Add("spotify-app-version", CLIENT_VERSION);
            request.Headers.Add("Origin", "https://open.spotify.com");
            request.Headers.Add("Referer", "https://open.spotify.com/");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonToken);
            request.Headers.Add("client-token", clientToken);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Spotify API query failed with status {Status}. Response: {Response}", response.StatusCode, responseText);
                    throw new HttpRequestException($"Spotify API query failed with status code {response.StatusCode}");
                }
                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while querying Spotify");
                throw;
            }
        }

        public async IAsyncEnumerable<SongSearchObject> SearchAnonymousAsync(string searchTerm, int limit = 20, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchTerm) || limit <= 0)
            {
                yield break;
            }
            limit = Math.Min(limit, 100); // Absolute max limit

            const int pageSize = 20;
            var yielded = 0;
            var offset = 0;

            while (yielded < limit)
            {
                var jsonBody = FormatTemplate(SearchTracksPayloadTemplate, searchTerm.Trim(), offset, pageSize);
                var responseText = await QuerySpotify(jsonBody, cancellationToken);

                var pageSongs = ParseSongs(responseText);
                var pageCount = 0;

                foreach (var song in pageSongs)
                {
                    yield return song;
                    yielded++;
                    pageCount++;

                    if (yielded >= limit)
                    {
                        yield break;
                    }
                }

                if (pageCount < pageSize)
                {
                    yield break;
                }

                offset += pageSize;
            }
        }

        public async IAsyncEnumerable<SongSearchObject> GetPlaylistAsync(string playlistId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int offset = 0;
            const int limit = 50;
            while (true)
            {
                var jsonBody = FormatTemplate(FetchPlaylistContentsPayloadTemplate, playlistId.Trim(), offset, limit);
                var responseText = await QuerySpotify(jsonBody, cancellationToken);
                using var doc = JsonDocument.Parse(responseText);
                if (!GetJsonNested(doc.RootElement, "data.playlistV2.content", out var content) ||
                    !content.TryGetProperty("items", out var itemsElement) ||
                    itemsElement.ValueKind != JsonValueKind.Array)
                {
                    yield break;
                }
                int count = 0;
                foreach (var item in itemsElement.EnumerateArray())
                {
                    if (GetJsonNested(item, "itemV3.data", out var trackData))
                    {
                        var song = BuildSongFromItemV3(trackData);
                        if (!string.IsNullOrEmpty(song.Title))
                        {
                            yield return song;
                        }
                    }
                    count++;
                }

                if (GetJsonNested(content, "pagingInfo.totalCount", out var totalCountElement) &&
                    totalCountElement.ValueKind == JsonValueKind.Number &&
                    totalCountElement.TryGetInt32(out var totalCount))
                {
                    if (offset + count >= totalCount)
                    {
                        yield break; // No more pages
                    }
                }

                if (count < limit)
                {
                    yield break; // No more pages
                }

                offset += limit;
            }
        }

        public async Task<SongSearchObject?> GetTrack(string trackId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return null;
            }

            var normalizedTrackId = trackId.Trim();
            const string trackUriPrefix = "spotify:track:";
            if (normalizedTrackId.StartsWith(trackUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedTrackId = normalizedTrackId[trackUriPrefix.Length..];
            }

            var jsonBody = FormatTemplate(GetTrackPayload, normalizedTrackId);
            var responseText = await QuerySpotify(jsonBody, cancellationToken);

            using var doc = JsonDocument.Parse(responseText);
            if (!GetJsonNested(doc.RootElement, "data.trackUnion", out var trackElement) ||
                trackElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var song = BuildSongFromTrackUnion(trackElement);
            return string.IsNullOrEmpty(song?.Title) ? null : song;
        }

        private static List<SongSearchObject> ParseSongs(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new List<SongSearchObject>();
            }

            using var doc = JsonDocument.Parse(rawJson);

            if (!GetJsonNested(doc.RootElement, "data.searchV2.tracksV2.items", out var itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<SongSearchObject>();

            foreach (var listItem in itemsElement.EnumerateArray())
            {
                SongSearchObject? song = null;

                if (GetJsonNested(listItem, "item.data", out var trackElement) &&
                    trackElement.ValueKind == JsonValueKind.Object)
                {
                    song = new SongSearchObject
                    {
                        Source = "spotify",
                        Title = GetString(trackElement, "name"),
                        Id = GetString(trackElement, "id"),
                        Artists = GetArtistsCsv(trackElement),
                        ImageLocation = GetImageLocation(trackElement),
                        ReleaseDate = "",
                        Duration = GetDurationSeconds(trackElement),
                        Rank = "0",
                        AlbumName = GetAlbumName(trackElement),
                        Explicit = false,
                        TrackPosition = "1",
                        Isrc = ""
                    };
                }

                if (song is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(song.Id) || !string.IsNullOrWhiteSpace(song.Title))
                {
                    results.Add(song);
                }
            }

            return results;
        }

        private sealed class TrackUnionData
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string Playcount { get; init; } = "0";
            public string TrackNumber { get; init; } = "1";
            public string AlbumName { get; init; } = string.Empty;
            public string ReleaseIsoString { get; init; } = string.Empty;
            public JsonElement DurationMsElement { get; init; }
            public JsonElement FirstArtistItemsElement { get; init; }
            public JsonElement OtherArtistItemsElement { get; init; }
            public JsonElement CoverSourcesElement { get; init; }
        }

        private static class TrackUnionPaths
        {
            public const string Id = "id";
            public const string Name = "name";
            public const string Playcount = "playcount";
            public const string TrackNumber = "trackNumber";
            public const string AlbumName = "albumOfTrack.name";
            public const string AlbumReleaseIsoString = "albumOfTrack.date.isoString";
            public const string DurationMs = "duration.totalMilliseconds";
            public const string FirstArtistItems = "firstArtist.items";
            public const string OtherArtistItems = "otherArtists.items";
            public const string CoverArtSources = "albumOfTrack.coverArt.sources";
            public const string ArtistProfile = "profile";
            public const string ArtistName = "name";
        }

        private static SongSearchObject? BuildSongFromTrackUnion(JsonElement trackElement)
        {
            var data = ExtractTrackUnionData(trackElement);

            if (string.IsNullOrWhiteSpace(data.Id) && string.IsNullOrWhiteSpace(data.Title))
            {
                return null;
            }

            return new SongSearchObject
            {
                Source = "spotify",
                Title = data.Title,
                Id = data.Id,
                Artists = GetTrackArtistsCsv(data.FirstArtistItemsElement, data.OtherArtistItemsElement),
                ImageLocation = GetTrackImageLocation(data.CoverSourcesElement),
                ReleaseDate = FormatIsoDate(data.ReleaseIsoString),
                Duration = GetDurationSecondsFromMillisecondsElement(data.DurationMsElement),
                Rank = data.Playcount,
                AlbumName = data.AlbumName,
                Explicit = false,
                TrackPosition = string.IsNullOrWhiteSpace(data.TrackNumber) ? "1" : data.TrackNumber,
                Isrc = string.Empty
            };
        }

        private static TrackUnionData ExtractTrackUnionData(JsonElement trackElement)
        {
            GetJsonNested(trackElement, TrackUnionPaths.DurationMs, out var durationMsElement);
            GetJsonNested(trackElement, TrackUnionPaths.FirstArtistItems, out var firstArtistItemsElement);
            GetJsonNested(trackElement, TrackUnionPaths.OtherArtistItems, out var otherArtistItemsElement);
            GetJsonNested(trackElement, TrackUnionPaths.CoverArtSources, out var coverSourcesElement);

            return new TrackUnionData
            {
                Id = GetString(trackElement, TrackUnionPaths.Id),
                Title = GetString(trackElement, TrackUnionPaths.Name),
                Playcount = GetStringOrNumber(trackElement, TrackUnionPaths.Playcount),
                TrackNumber = GetStringOrNumber(trackElement, TrackUnionPaths.TrackNumber, "1"),
                AlbumName = GetStringNested(trackElement, TrackUnionPaths.AlbumName),
                ReleaseIsoString = GetStringNested(trackElement, TrackUnionPaths.AlbumReleaseIsoString),
                DurationMsElement = durationMsElement,
                FirstArtistItemsElement = firstArtistItemsElement,
                OtherArtistItemsElement = otherArtistItemsElement,
                CoverSourcesElement = coverSourcesElement
            };
        }

        private static string GetTrackArtistsCsv(JsonElement firstArtistItemsElement, JsonElement otherArtistItemsElement)
        {
            var names = new List<string>();
            AddArtistNames(firstArtistItemsElement, names);
            AddArtistNames(otherArtistItemsElement, names);
            return string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void AddArtistNames(JsonElement artistItemsElement, List<string> names)
        {
            if (artistItemsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var artistItem in artistItemsElement.EnumerateArray())
            {
                if (!GetJsonNested(artistItem, TrackUnionPaths.ArtistProfile, out var profileElement))
                {
                    continue;
                }

                var name = GetString(profileElement, TrackUnionPaths.ArtistName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        private static string? GetTrackImageLocation(JsonElement coverSourcesElement)
        {
            if (coverSourcesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? fallback = null;
            foreach (var source in coverSourcesElement.EnumerateArray())
            {
                var url = GetString(source, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                fallback ??= url;

                if (source.TryGetProperty("width", out var widthElement) &&
                    widthElement.ValueKind == JsonValueKind.Number &&
                    widthElement.TryGetInt32(out var width) &&
                    width == 300)
                {
                    return url;
                }
            }

            return fallback;
        }

        private static string GetDurationSecondsFromMillisecondsElement(JsonElement durationMsElement)
        {
            if (durationMsElement.ValueKind == JsonValueKind.Number &&
                durationMsElement.TryGetDouble(out var totalMs))
            {
                return ((int)Math.Round(totalMs / 1000.0)).ToString();
            }

            return "0";
        }

        private static string GetArtistsCsv(JsonElement trackElement)
        {
            if (!GetJsonNested(trackElement, "artists.items", out var artistItems) ||
                artistItems.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var names = new List<string>();
            foreach (var artistItem in artistItems.EnumerateArray())
            {
                if (GetJsonNested(artistItem, "profile", out var profileElement))
                {
                    var name = GetString(profileElement, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return string.Join(", ", names);
        }

        private static string? GetImageLocation(JsonElement trackElement)
        {
            if (!GetJsonNested(trackElement, "albumOfTrack.coverArt.sources", out var sourcesElement) ||
                sourcesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? fallback = null;
            foreach (var source in sourcesElement.EnumerateArray())
            {
                var url = GetString(source, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (fallback is null)
                {
                    fallback = url;
                }

                if (source.TryGetProperty("width", out var widthElement) &&
                    widthElement.ValueKind == JsonValueKind.Number &&
                    widthElement.TryGetInt32(out var width) &&
                    width == 300)
                {
                    return url;
                }
            }

            return fallback;
        }

        private static string GetDurationSeconds(JsonElement trackElement)
        {
            if (GetJsonNested(trackElement, "duration.totalMilliseconds", out var totalMsElement) &&
                totalMsElement.ValueKind == JsonValueKind.Number &&
                totalMsElement.TryGetDouble(out var totalMs))
            {
                return ((int)Math.Round(totalMs / 1000.0)).ToString();
            }

            return "0";
        }

        private static string GetAlbumName(JsonElement trackElement)
        {
            if (GetJsonNested(trackElement, "albumOfTrack", out var albumElement))
            {
                return GetString(albumElement, "name");
            }

            return string.Empty;
        }

        private static SongSearchObject BuildSongFromItemV3(JsonElement entityElement)
        {
            var uri = GetString(entityElement, "uri");
            return new SongSearchObject
            {
                Source = "spotify",
                Title = GetItemV3Title(entityElement),
                Artists = GetItemV3ArtistsCsv(entityElement),
                ImageLocation = GetItemV3ImageLocation(entityElement),
                Id = GetSpotifyIdFromUri(uri),
                ReleaseDate = GetItemV3ReleaseDate(entityElement),
                Duration = GetItemV3DurationSeconds(entityElement),
                Rank = "0",
                AlbumName = GetItemV3AlbumName(entityElement),
                Explicit = false,
                TrackPosition = "1",
                Isrc = null
            };
        }

        private static string GetItemV3Title(JsonElement entityElement)
        {
            if (GetJsonNested(entityElement, "identityTrait", out var identityTraitElement))
            {
                return GetString(identityTraitElement, "name");
            }

            return string.Empty;
        }

        private static string GetItemV3ArtistsCsv(JsonElement entityElement)
        {
            if (!GetJsonNested(entityElement, "identityTrait.contributors.items", out var contributorItems) ||
                contributorItems.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var names = new List<string>();
            foreach (var contributor in contributorItems.EnumerateArray())
            {
                var name = GetString(contributor, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return string.Join(", ", names);
        }

        private static string? GetItemV3ImageLocation(JsonElement entityElement)
        {
            if (!GetJsonNested(entityElement, "visualIdentityTrait.squareCoverImage.image.data.sources", out var sourcesElement) ||
                sourcesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? fallback = null;
            foreach (var source in sourcesElement.EnumerateArray())
            {
                var url = GetString(source, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (fallback is null)
                {
                    fallback = url;
                }

                if (source.TryGetProperty("maxWidth", out var widthElement) &&
                    widthElement.ValueKind == JsonValueKind.Number &&
                    widthElement.TryGetInt32(out var width) &&
                    width == 300)
                {
                    return url;
                }
            }

            return fallback;
        }

        private static string GetItemV3ReleaseDate(JsonElement entityElement)
        {
            if (GetJsonNested(entityElement, "identityTrait.contentHierarchyParent.publishingMetadataTrait.firstPublishedAt", out var firstPublishedAtElement))
            {
                var isoString = GetString(firstPublishedAtElement, "isoString");
                if (!string.IsNullOrWhiteSpace(isoString))
                {
                    return isoString;
                }
            }

            return string.Empty;
        }

        private static string GetItemV3DurationSeconds(JsonElement entityElement)
        {
            if (GetJsonNested(entityElement, "consumptionExperienceTrait.duration.seconds", out var secondsElement) &&
                secondsElement.ValueKind == JsonValueKind.Number &&
                secondsElement.TryGetInt32(out var seconds))
            {
                return seconds.ToString();
            }

            return "0";
        }

        private static string GetItemV3AlbumName(JsonElement entityElement)
        {
            if (GetJsonNested(entityElement, "identityTrait.contentHierarchyParent.identityTrait", out var parentIdentityTraitElement))
            {
                return GetString(parentIdentityTraitElement, "name");
            }

            return string.Empty;
        }

        private static bool GetJsonNested(JsonElement root, string path, out JsonElement jsonNested)
        {
            if (path.StartsWith('.')) 
                path = path[1..];
            var keys = path.Split('.');

            jsonNested = root;
            foreach (var key in keys)
            {
                if (jsonNested.TryGetProperty(key, out var next))
                {
                    jsonNested = next;
                } else
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetSpotifyIdFromUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return string.Empty;
            }

            const string prefix = "spotify:track:";
            if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return uri[prefix.Length..];
            }

            return uri;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var propertyElement) &&
                propertyElement.ValueKind == JsonValueKind.String)
            {
                return propertyElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string GetStringNested(JsonElement root, string path)
        {
            if (GetJsonNested(root, path, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string FormatIsoDate(string isoString)
        {
            if (string.IsNullOrWhiteSpace(isoString))
            {
                return string.Empty;
            }

            if (DateTime.TryParse(isoString, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd");
            }

            return isoString;
        }

        private static string GetStringOrNumber(JsonElement element, string propertyName, string fallback = "0")
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(propertyName, out var propertyElement))
            {
                return fallback;
            }

            return propertyElement.ValueKind switch
            {
                JsonValueKind.String => propertyElement.GetString() ?? fallback,
                JsonValueKind.Number => propertyElement.GetRawText(),
                _ => fallback
            };
        }

        private static string FormatTemplate(string template, params object[] args)
        {
            var result = template;
            for (int i = 0; i < args.Length; i++)
            {
                result = result.Replace($"${i}", args[i]?.ToString() ?? string.Empty);
            }
            return result;
        }
    }
}