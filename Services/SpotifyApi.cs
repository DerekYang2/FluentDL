using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using QobuzApiSharp.Models.Content;
using SpotifyAPI.Web;

namespace FluentDL.Services
{
    internal class SpotifyApi
    {
        private static SpotifyClientConfig config = SpotifyClientConfig.CreateDefault();
        private static SpotifyClient spotify;

        public SpotifyApi()
        {
        }

        public static async Task Initialize(string? clientId, string? clientSecret)
        {
            // TODO: if do not exist, message should be shown
            if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                var request = new ClientCredentialsRequest(clientId, clientSecret);
                var response = await new OAuthClient(config).RequestToken(request);

                spotify = new SpotifyClient(config.WithToken(response.AccessToken));
            }
        }

        public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int limit = 25)
        {
            // Trim
            artistName = artistName.Trim();
            trackName = trackName.Trim();
            albumName = albumName.Trim();
            if (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
            {
                return;
            }

            var reqStr = "";
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                reqStr += $"artist:{artistName} ";
            }

            if (!string.IsNullOrWhiteSpace(trackName))
            {
                reqStr += $"track:{trackName} ";
            }

            if (!string.IsNullOrWhiteSpace(albumName))
            {
                reqStr += $"album:{albumName} ";
            }

            reqStr = reqStr.Trim(); // Trim the query

            var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) { Limit = limit }, token);

            if (response.Tracks.Items == null)
            {
                return;
            }

            itemSource.Clear(); // Clear the item source
            foreach (FullTrack track in response.Tracks.Items)
            {
                if (token.IsCancellationRequested) return;
                var song = await Task.Run(() => ConvertSongSearchObject(track), token);
                if (song != null)
                {
                    itemSource.Add(song);
                }
            }
        }

        public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25)
        {
            query = query.Trim(); // Trim the query
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            limit = Math.Min(limit, 50); // Limit to 50 (maximum for this api)
            var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, query) { Limit = limit }, token);

            if (response.Tracks.Items == null)
            {
                return;
            }

            itemSource.Clear(); // Clear the item source
            foreach (FullTrack track in response.Tracks.Items)
            {
                if (token.IsCancellationRequested) return;
                var song = await Task.Run(() => ConvertSongSearchObject(track), token);
                if (song != null)
                {
                    itemSource.Add(song);
                }
            }
        }

        public static async Task<string?> GetPlaylistName(string playlistId)
        {
            var playlist = await spotify.Playlists.Get(playlistId);
            var playlistName = playlist.Name;
            return playlistName;
        }

        public static async Task<FullTrack?> GetTrackFromISRC(string isrc)
        {
            // https://api.spotify.com/v1/search?type=track&q=isrc:{isrc}
            var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, $"isrc:{isrc}"));
            Debug.WriteLine(response.Tracks.Items.Count);
            if (response.Tracks.Items == null)
            {
                return null;
            }

            // Loop through and check if isrc matches
            foreach (var track in response.Tracks.Items)
            {
                if (track.ExternalIds["isrc"] == isrc)
                {
                    return track;
                }
            }

            return null;
        }

        public static async Task<FullTrack?> GetTrack(string id)
        {
            var track = await spotify.Tracks.Get(id);
            return track;
        }

        // TODO: handle invalid playlist ids
        public static async Task<List<SongSearchObject>> GetPlaylist(string playlistId, CancellationToken token)
        {
            var pages = await spotify.Playlists.GetItems(playlistId, cancel: token);
            var allPages = await spotify.PaginateAll(pages, cancellationToken: token);

            var songs = new List<SongSearchObject>();
            // Debug: loop and print all tracks
            foreach (PlaylistTrack<IPlayableItem> item in allPages)
            {
                if (item.Track is FullTrack track)
                {
                    if (token.IsCancellationRequested)
                    {
                        break; // Stop if cancelled
                    }

                    // All FullTrack properties are available
                    var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
                    if (artistCsv.Length == 0 || track.Album.Name.Length == 0)
                    {
                        continue;
                    }

                    var songObj = ConvertSongSearchObject(track);
                    if (songObj != null)
                    {
                        songs.Add(songObj);
                    }
                }
            }

            return songs;
        }

        public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate)
        {
            var id = url.Split("/").Last();
            // Remove any query parameters
            if (id.Contains("?"))
            {
                id = id.Split("?").First();
            }

            if (url.StartsWith("https://open.spotify.com/playlist/"))
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"Loading playlist \"{await GetPlaylistName(id)}\" ...");

                var pages = await spotify.Playlists.GetItems(id, cancel: token);
                var allPages = await spotify.PaginateAll(pages, cancellationToken: token);
                itemSource.Clear(); // Clear the item source

                // Debug: loop and print all tracks
                foreach (PlaylistTrack<IPlayableItem> item in allPages)
                {
                    if (item.Track is FullTrack track)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break; // Stop if cancelled
                        }

                        var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                        if (songObj != null)
                        {
                            itemSource.Add(songObj);
                        }
                    }
                }
            }

            if (url.StartsWith("https://open.spotify.com/album/"))
            {
                var album = await spotify.Albums.Get(id, token);
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"Loading album \"{album.Name}\" ...");

                var pages = album.Tracks;
                var allPages = await spotify.PaginateAll(pages, cancellationToken: token);

                itemSource.Clear(); // Clear the item source

                foreach (var simpleTrack in allPages)
                {
                    if (token.IsCancellationRequested)
                    {
                        break; // Stop if cancelled
                    }

                    // Get full track
                    var track = await spotify.Tracks.Get(simpleTrack.Id);
                    var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                    if (songObj != null)
                    {
                        itemSource.Add(songObj);
                    }
                }
            }

            if (url.StartsWith("https://open.spotify.com/track/")) // Single track, no need to clear item source
            {
                var fullTrack = await spotify.Tracks.Get(id, token);
                var songObj = ConvertSongSearchObject(fullTrack);
                if (songObj != null)
                {
                    itemSource.Add(songObj);
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"Loaded track \"{fullTrack.Name}\"");
                }
            }
        }

        public static async Task<SongSearchObject?> GetSpotifyTrack(SongSearchObject song)
        {
            // Try to find by ISRC first
            if (song.Isrc != null)
            {
                var track = await GetTrackFromISRC(song.Isrc);
                if (track != null)
                {
                    return ConvertSongSearchObject(track);
                }
            }

            return null;
        }

        // NOTE: album images are 640, 300, then 64 
        public static SongSearchObject? ConvertSongSearchObject(FullTrack track)
        {
            var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
            if (artistCsv.Length == 0 || track.Album.Name.Length == 0)
            {
                return null;
            }

            return new SongSearchObject
            {
                Source = "spotify",
                Title = track.Name,
                Artists = artistCsv,
                ImageLocation = track.Album.Images.Last().Url, // Smallest image, 64 x 64
                Id = track.Id,
                ReleaseDate = track.Album.ReleaseDate,
                Duration = ((int)Math.Round(track.DurationMs / 1000.0)).ToString(),
                Rank = track.Popularity.ToString(),
                AlbumName = track.Album.Name,
                Explicit = track.Explicit,
                TrackPosition = track.TrackNumber.ToString(),
                Isrc = track.ExternalIds["isrc"],
            };
        }
    }
}