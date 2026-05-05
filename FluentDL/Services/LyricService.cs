using FluentDL.Models;
using FluentDL.Contracts.Services;
using FluentDL.ViewModels;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FluentDL.Services
{
    public interface ILyricService
    {
        Task<string?> GetLyricsAsync(SongSearchObject songSearchObject);

    }

    public class LRCLyricService(HttpClient httpClient, ILocalSettingsService localSettingsService) : ILyricService
    {
        private readonly string _baseUrl = "https://lrclib.net/api";
        private readonly HttpClient _httpClient = httpClient;
        private readonly ILocalSettingsService _localSettingsService = localSettingsService;

        public async Task<string?> GetLyricsAsync(SongSearchObject songSearchObject)
        {
            var artistsStr = songSearchObject.Artists ?? string.Empty;
            var artistsSplit = string.IsNullOrWhiteSpace(artistsStr) 
                ? [string.Empty] 
                : artistsStr.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (artistsSplit.Count == 0) artistsSplit.Add(string.Empty);

            var title = songSearchObject.Title ?? string.Empty;
            var album = songSearchObject.AlbumName ?? string.Empty;
            var duration = songSearchObject.Duration;
            var pref = await _localSettingsService.ReadSettingAsync<int?>(SettingsViewModel.LyricsPreference) ?? 2;

            string? foundSynced = null;
            string? foundUnsynced = null;

            foreach (var artist in artistsSplit)
            {
                var escapedArtist = Uri.EscapeDataString(artist);
                var escapedTitle = Uri.EscapeDataString(title);
                var escapedAlbum = Uri.EscapeDataString(album);

                var url = $"{_baseUrl}/get?artist_name={escapedArtist}&track_name={escapedTitle}&album_name={escapedAlbum}&duration={duration}";

                var response = await _httpClient.GetAsync(url);
                LrcResponse? responseObj = null;

                if (response.IsSuccessStatusCode)
                {
                    responseObj = await response.Content.ReadFromJsonAsync<LrcResponse>();
                    if (responseObj != null)
                    {
                        if (responseObj.Instrumental)
                        {
                            return string.Empty; // Return empty string for instrumental tracks
                        }

                        foundSynced = responseObj.SyncedLyrics;
                        foundUnsynced ??= responseObj.PlainLyrics;
                    }

                    if (foundSynced != null && pref > 0)
                    {
                        return foundSynced;
                    }
                    if (foundUnsynced != null && pref == 0)
                    {
                        return foundUnsynced;
                    }
                }
            }

            // Attempt search if nothing from /get
            foreach (var artist in artistsSplit)
            {
                var escapedArtist = Uri.EscapeDataString(artist);
                var escapedTitle = Uri.EscapeDataString(title);
                var escapedAlbum = Uri.EscapeDataString(album);

                // Fallback to /api/search
                var searchUrl = $"{_baseUrl}/search?track_name={escapedTitle}&artist_name={escapedArtist}";
                var searchResponse = await _httpClient.GetAsync(searchUrl);
                if (searchResponse.IsSuccessStatusCode)
                {
                    var searchResults = await searchResponse.Content.ReadFromJsonAsync<List<LrcResponse>>();
                    if (searchResults != null)
                    {
                        // Filter by duration (+- 1 sec), matching track name, and matching artist name (already searching by artist and track though)
                        IEnumerable<LrcResponse> responseObjs = searchResults
                            .Where(r =>
                                double.TryParse(duration, out double d) && Math.Abs(r.Duration - d) <= 1 &&
                                r.TrackName?.Equals(title, StringComparison.OrdinalIgnoreCase) == true &&
                                r.ArtistName?.Equals(artist, StringComparison.OrdinalIgnoreCase) == true);

                        foreach (var obj in responseObjs)
                        {
                            if (obj.Instrumental)
                            {
                                return string.Empty;
                            }
                            foundSynced = obj.SyncedLyrics;
                            foundUnsynced ??= obj.PlainLyrics;

                            if (foundSynced != null && pref > 0)
                            {
                                return foundSynced;
                            }
                            if (foundUnsynced != null && pref == 0)
                            {
                                return foundUnsynced;
                            }
                        }
                    }
                }
            }

            // if fallback allowed, return found unsynced
            if (pref == 2)
            {
                return foundUnsynced;
            }
            else
            {
                // strictly synced or unsynced only reaches here if not found
                return null;
            }
        }
    }

    public class LrcResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("albumName")]
        public string? AlbumName { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }
}
