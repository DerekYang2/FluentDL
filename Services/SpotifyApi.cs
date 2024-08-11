using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DeezNET.Data;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
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
                try
                {
                    var response = await new OAuthClient(config).RequestToken(request);
                    spotify = new SpotifyClient(config.WithToken(response.AccessToken));
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to initialize Spotify API: " + e.Message);
                }
            }
        }

        private static bool CloseMatch(string str1, string str2)
        {
            return ApiHelper.IsSubstring(str1.ToLower(), str2.ToLower());
        }

        // For whatever reason, a request like below throws bad request error
        // REQUEST: artist:"The Beep Test" track:"The Beep Test: 20 Metre (Complete Test)" album:"The Beep Test: The Best 20 Metre and 15 Metre Bleep Test for Personal Fitness & Recruitment Practice to the Police, RAF, Army, Fire Brigade, Royal Air Force, Royal Navy and the Emergency Services"

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
                reqStr += $"artist:\"{artistName}\" ";
            }

            if (!string.IsNullOrWhiteSpace(trackName))
            {
                reqStr += $"track:\"{trackName}\" ";
            }

            if (!string.IsNullOrWhiteSpace(albumName))
            {
                reqStr += $"album:\"{albumName}\" ";
            }

            reqStr = reqStr.Trim(); // Trim the query

            SearchResponse? response;
            try
            {
                response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) { Limit = limit }, token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to advanced search: " + e.Message);
                // Remove album: and everything after
                var albumIdx = reqStr.IndexOf("album:");
                if (albumIdx != -1)
                {
                    reqStr = reqStr.Substring(0, albumIdx);
                    response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) { Limit = 20 }, token);
                }
                else
                {
                    return;
                }
            }

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

        public static async Task<FullTrack?> GetTrackFromISRC(string isrc, CancellationToken token = default)
        {
            // https://api.spotify.com/v1/search?type=track&q=isrc:{isrc}
            var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, $"isrc:{isrc}"), token);
            if (token.IsCancellationRequested)
            {
                return null;
            }

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
            try
            {
                var track = await spotify.Tracks.Get(id);
                return track;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return null;
            }
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

        public static async Task<SongSearchObject?> GetSpotifyTrack(SongSearchObject song, CancellationToken token = default, ConversionUpdateCallback? callback = null)
        {
            // Try to find by ISRC first
            if (song.Isrc != null)
            {
                var track = await GetTrackFromISRC(song.Isrc, token);
                if (track != null)
                {
                    var retObj = ConvertSongSearchObject(track);
                    if (retObj != null) // Update callback with result
                    {
                        callback?.Invoke(InfoBarSeverity.Success, retObj);
                    }

                    return retObj;
                }
            }

            // Find by metadata
            var artistCSV = song.Artists;
            var artists = artistCSV.Split(", ");
            var trackName = song.Title;
            var albumName = song.AlbumName;

            var reqStr = "";
            if (!string.IsNullOrWhiteSpace(artistCSV))
            {
                reqStr += $"artist:\"{artistCSV}\" ";
            }

            if (!string.IsNullOrWhiteSpace(trackName))
            {
                reqStr += $"track:\"{trackName}\" ";
            }

            if (!string.IsNullOrWhiteSpace(albumName))
            {
                reqStr += $"album:\"{albumName}\" ";
            }

            reqStr = reqStr.Trim(); // Trim the query
            var songObjList = new List<SongSearchObject>(); // List of SongSearchObject results
            HashSet<string> idSet = new HashSet<string>(); // Set of track ids

            SearchResponse? response;
            try
            {
                response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) { Limit = 20 }, token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to advanced search: " + e.Message);
                // Remove album: and everything after
                var albumIdx = reqStr.IndexOf("album:");
                if (albumIdx != -1)
                {
                    reqStr = reqStr.Substring(0, albumIdx);
                    response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) { Limit = 20 }, token);
                }
                else
                {
                    return null;
                }
            }

            if (response.Tracks.Items != null)
            {
                foreach (FullTrack track in response.Tracks.Items) // Add to songObjList if 
                {
                    if (token.IsCancellationRequested) return null;

                    var songObject = ConvertSongSearchObject(track);

                    if (songObject == null) continue;

                    if (!idSet.Contains(songObject.Id))
                    {
                        idSet.Add(songObject.Id);

                        var oneArtistMatch = artists.Any(a => track.Artists.Any(ta => CloseMatch(a, ta.Name)));

                        if (oneArtistMatch)
                        {
                            songObjList.Add(songObject);
                        }
                    }
                }
            }

            // Pass 1: exact title match, find least edit distance album name
            SongSearchObject? closeMatchObj = null;
            int minEditDistance = int.MaxValue;
            foreach (var songObj in songObjList)
            {
                if (token.IsCancellationRequested) // Cancel requested, terminate this method
                {
                    return null;
                }

                var titlePruned = ApiHelper.PruneTitle(trackName);
                var songObjTitlePruned = ApiHelper.PruneTitle(songObj.Title);
                if (titlePruned.Equals(songObjTitlePruned) || titlePruned.Replace("radioedit", "").Equals(songObjTitlePruned.Replace("radioedit", ""))) // If the title matches without punctuation
                {
                    if (albumName.ToLower().Replace(" ", "").Equals(songObj.AlbumName.ToLower().Replace(" ", ""))) // If the album name is exact match
                    {
                        callback?.Invoke(InfoBarSeverity.Warning, songObj);
                        return songObj; // Return direct match
                    }

                    var dist = ApiHelper.CalcLevenshteinDistance(ApiHelper.PruneTitle(albumName), ApiHelper.PruneTitle(songObj.AlbumName));
                    if (dist < minEditDistance)
                    {
                        minEditDistance = dist;
                        closeMatchObj = songObj;
                    }
                }
            }

            if (closeMatchObj != null) // Return the least edit distance album name
            {
                callback?.Invoke(InfoBarSeverity.Warning, closeMatchObj);
                return closeMatchObj;
            }

            callback?.Invoke(InfoBarSeverity.Error, song); // Show error with original song object
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

        public static async Task<SortedSet<string>> GetGenres(List<SimpleArtist> artists)
        {
            var genreSet = new SortedSet<string>();
            foreach (var artist in artists)
            {
                var fullArtist = await spotify.Artists.Get(artist.Id);
                foreach (var genre in fullArtist.Genres)
                {
                    genreSet.Add(genre);
                }
            }

            return genreSet;
        }

        public static async Task DownloadEquivalentTrack(string filePath, SongSearchObject song, bool strict = true, ConversionUpdateCallback? callback = default)
        {
            SongSearchObject? equivalent = await DeezerApi.GetDeezerTrack(song, onlyISRC: strict); // Try Deezer first

            // 0 - mp3, 1 - flac
            var settingIdx = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.SpotifyQuality);

            if (equivalent != null) // Found on Deezer
            {
                var bitrateEnum = settingIdx switch
                {
                    0 => DeezNET.Data.Bitrate.MP3_320,
                    _ => DeezNET.Data.Bitrate.FLAC,
                };

                await DeezerApi.DownloadTrack(filePath, equivalent, bitrateEnum);
                callback?.Invoke(InfoBarSeverity.Success, song);
                return;
            }

            // Not found on Deezer, try Qobuz
            equivalent = await QobuzApi.GetQobuzTrack(song, onlyISRC: strict);


            if (equivalent != null) // Found on Qobuz
            {
                await QobuzApi.DownloadTrack(filePath, equivalent);
                callback?.Invoke(InfoBarSeverity.Success, song);
                return;
            }

            if (strict) // If strict, do not try youtube
            {
                callback?.Invoke(InfoBarSeverity.Error, song); // Show error, not downloaded
                return;
            }

            // Not found on Qobuz, try youtube
            equivalent = await YoutubeApi.GetYoutubeTrack(song);

            if (equivalent != null) // Found on Youtube
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var opusLocation = Path.Combine(directory, fileName + ".opus");

                await YoutubeApi.DownloadAudio(opusLocation, song.Id); // Download audio as opus
                await FFmpegRunner.ConvertToFlac(opusLocation); // Convert opus to flac
                callback?.Invoke(InfoBarSeverity.Warning, song); // Not perfect match
                return;
            }

            callback?.Invoke(InfoBarSeverity.Error, song); // Show error, not downloaded
        }


        public static async Task UpdateMetadata(string filePath, string id)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var track = await GetTrack(id);
            if (track == null)
            {
                return;
            }

            var fullAlbum = await spotify.Albums.Get(track.Album.Id); // Get the full album for genres + upc
            var artistList = track.Artists.Select(a => a.Name);
            var albumArtistList = fullAlbum.Artists.Select(a => a.Name);
            var genreList = await GetGenres(track.Artists);

            var metadata = new MetadataObject(filePath)
            {
                Title = track.Name,
                Artists = artistList.ToArray(),
                AlbumName = track.Album.Name,
                AlbumArtists = albumArtistList.ToArray(),
                Isrc = track.ExternalIds["isrc"],
                ReleaseDate = DateTime.Parse(track.Album.ReleaseDate),
                AlbumArtPath = track.Album.Images.First().Url, // First is the largest image
                Genres = genreList.ToArray(),
                TrackNumber = track.TrackNumber,
                TrackTotal = track.Album.TotalTracks,
                Upc = fullAlbum.ExternalIds["upc"],
                Url = track.Uri,
            };

            await metadata.SaveAsync();
        }
    }
}