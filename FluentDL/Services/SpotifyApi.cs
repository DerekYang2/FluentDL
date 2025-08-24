using AngleSharp.Text;
using DeezNET.Data;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentDL.Services
{
    internal class SpotifyApi
    {
        private static SpotifyClientConfig config = SpotifyClientConfig.CreateDefault().WithRetryHandler(new SimpleRetryHandler() { RetryTimes = 3, TooManyRequestsConsumesARetry = false});
        private static SpotifyClient spotify;
        private static Random rand = new Random();
        private static HttpClient httpClient = new HttpClient();
        public static bool IsInitialized = false;

        public SpotifyApi()
        {

        }

        public static async Task Initialize(string? clientId, string? clientSecret)
        {
            IsInitialized = false;

            // First try to use clientId and clientSecret from settings
            if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                try
                {
                    spotify = await Task.Run(() => new SpotifyClient(config.WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret))));
                    IsInitialized = true;
                    return;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to initialize Spotify API: " + e.Message);
                }
            }
            
            // If clientId and clientSecret are not provided, try to get from browser
            try
            {
                spotify = await Task.Run(() => new SpotifyClient(config.WithAuthenticator(new EmbedAuthenticator())));
                IsInitialized = true;
                return;
            } catch (Exception e) {
                Debug.WriteLine("Failed to get access token: " + e.Message);
            }
            
            // If still not initialized, try to get bundled keys
            try {
                var idList = KeyReader.GetValues("spot_id");
                var secretList = KeyReader.GetValues("spot_secret");

                if (!idList.IsEmpty && !secretList.IsEmpty && idList.Length == secretList.Length)  // If lists have content and same length
                {
                    var randIdx = rand.Next(0, idList.Length);
                    var id = idList[randIdx];
                    var secret = secretList[randIdx];

                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))  
                    {
                        throw new Exception("invalid/empty bundled key in list");
                    }

                    spotify = await Task.Run(() => new SpotifyClient(config.WithAuthenticator(new ClientCredentialsAuthenticator(id, secret))));
                    IsInitialized = true;
                }
            } catch (Exception e) {
                Debug.WriteLine("Failed to initialize Spotify API: " + e.Message);
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
            if (!IsInitialized || (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0)) // If no search query
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
                response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr), token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to advanced search: " + e.Message);
                // Remove album: and everything after
                var albumIdx = reqStr.IndexOf("album:");
                if (albumIdx != -1)
                {
                    reqStr = reqStr.Substring(0, albumIdx);
                    response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr), token);
                }
                else
                {
                    return;
                }
            }

            if (response.Tracks == null || response.Tracks.Items == null)
            {
                return;
            }
            
            
            int count = 0;
            int totalCount = 0;
            await foreach (FullTrack track in spotify.Paginate(response.Tracks, (s) => s.Tracks)) {
                if (token.IsCancellationRequested || count >= limit || totalCount >= 100) return;
                var song = await Task.Run(() => ConvertSongSearchObject(track), token);
                if (song != null)
                {
                    itemSource.Add(song);
                    count++;  // Keep track of valid iterations
                }
                totalCount++;  // On every iteration
            }
        }

        public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25, bool albumMode = false)
        {
            query = query.Trim(); // Trim the query
            if (!IsInitialized || string.IsNullOrWhiteSpace(query))
            {
                return;
            }


            limit = Math.Min(limit, 50); // Limit to 50 (maximum for this api)
            SearchResponse? response = null;
            try
            {
                response = await spotify.Search.Item(new SearchRequest(albumMode ? SearchRequest.Types.Album : SearchRequest.Types.Track, query), token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to general search: " + e.Message);
                return; // If failed, return
            }

            if (response?.Tracks?.Items == null && response?.Albums?.Items == null)
            {
                return;
            }

            itemSource.Clear(); // Clear the item source

            int notNullCount = 0;
            int totalCount = 0;

            try
            {
                if (albumMode)
                {
                    await foreach (var album in spotify.Paginate(response.Albums, s => s.Albums))
                    {
                        if (token.IsCancellationRequested || notNullCount >= limit || totalCount >= 100) return;
                        var albumObj = ConvertAlbumSearchObject(await spotify.Albums.Get(album.Id));
                        
                        if (albumObj != null)
                        {
                            itemSource.Add(albumObj);
                            notNullCount++;
                        }
                        totalCount++;  // On every iteration
                    }
                }
                else
                {
                    await foreach (FullTrack track in spotify.Paginate(response.Tracks, s => s.Tracks))
                    {
                        if (token.IsCancellationRequested || notNullCount >= limit || totalCount >= 100) return;
                        var song = ConvertSongSearchObject(track);
                        if (song != null)
                        {
                            itemSource.Add(song);
                            notNullCount++;  // Keep track of valid iterations
                        }
                        totalCount++;  // On every iteration
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed general search: " + e.ToString());
            }
        }

        public static async Task<string?> GetPlaylistName(string playlistId)
        {
            try
            {
                var playlist = await spotify.Playlists.Get(playlistId);
                var playlistName = playlist.Name;
                return playlistName;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to get playlist name: " + e.Message);
                return null;
            }
        }

        public static async Task<FullTrack?> GetTrackFromISRC(string isrc, CancellationToken token = default)
        {
            try {
                // https://api.spotify.com/v1/search?type=track&q=isrc:{isrc}
                var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, $"isrc:{isrc}"), token);

                if (token.IsCancellationRequested)
                {
                    return null;
                }

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
            } catch (Exception e)
            {
                Debug.WriteLine("Failed to get track from ISRC: " + e.Message);
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

        public static async Task<FullAlbum?> GetAlbum(string id)
        {
            try
            {
                var album = await spotify.Albums.Get(id);
                return album;
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

        public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
        {
            if (!IsInitialized)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Error, "Spotify API not initialized. Go to settings to authenticate.");
                return;
            }

            var id = url.Split("/").Last();
            // Remove any query parameters
            if (id.Contains("?"))
            {
                id = id.Split("?").First();
            }

            if (url.StartsWith("https://open.spotify.com/playlist/"))
            {
                var playlistName = await GetPlaylistName(id);
                if (playlistName == null)
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>Spotify</b>   Failed to load playlist <a href='{url}'>{url}</a>");
                    return;
                }

                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Spotify</b>   Loading playlist <a href='{url}'>{playlistName}</a>", -1);

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
                            statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Spotify</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>");
                            return; // Stop if cancelled
                        }

                        var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                        if (songObj != null)
                        {
                            itemSource.Add(songObj);
                        }
                    }
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Spotify</b>   Loaded playlist <a href='{url}'>{playlistName}</a>");
            }

            if (url.StartsWith("https://open.spotify.com/album/"))
            {
                var album = await spotify.Albums.Get(id, token);
                
                if (albumMode)
                {
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                    return;
                }

                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Spotify</b>   Loading album <a href='{url}'>{album.Name}</a>", -1);

                var pages = album.Tracks;
                var allPages = await spotify.PaginateAll(pages, cancellationToken: token);

                itemSource.Clear(); // Clear the item source

                foreach (var simpleTrack in allPages)
                {
                    if (token.IsCancellationRequested)
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Spotify</b>   Cancelled loading album <a href='{url}'>{album.Name}</a>");
                        return; // Stop if cancelled
                    }

                    // Get full track
                    var track = await spotify.Tracks.Get(simpleTrack.Id);
                    var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                    if (songObj != null)
                    {
                        itemSource.Add(songObj);
                    }
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Spotify</b>   Loaded album <a href='{url}'>{album.Name}</a>");
            }

            if (url.StartsWith("https://open.spotify.com/track/")) // Single track, no need to clear item source
            {
                var fullTrack = await spotify.Tracks.Get(id, token);
                var songObj = ConvertSongSearchObject(fullTrack);
                if (songObj != null)
                {
                    itemSource.Add(songObj);
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Spotify</b>   Loaded track <a href='{url}'>{fullTrack.Name}</a>");
                }
            }
        }

        // TODO: if 20 was not hit/searches are very low, then do not do a spotify filter search, instead append artist and track name as general query
        public static async Task<SongSearchObject?> GetSpotifyTrack(SongSearchObject song, CancellationToken token = default, ConversionUpdateCallback? callback = null)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(InfoBarSeverity.Error, song);
                return null;
            }

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
                if (reqStr.Length >= 250)
                {  // Maximum query is 250 characters
                    reqStr = reqStr.Substring(0, 250);
                }
                response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) {Limit=20 }, token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to advanced search: " + e.Message);
                // Remove album: and everything after
                var albumIdx = reqStr.IndexOf("album:");
                if (albumIdx != -1)
                {
                    reqStr = reqStr.Substring(0, albumIdx);
                    response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr) {Limit=20 }, token);
                }
                else
                {
                    return null;
                }
            }
            
            int notNullCount = 0, totalCount = 0;
            int LIMIT = 20;

            if (response.Tracks != null)
            {

                await foreach (FullTrack track in spotify.Paginate(response.Tracks, (s) => s.Tracks)) {
                    if (token.IsCancellationRequested) return null;

                    if (notNullCount >= LIMIT || totalCount >= 1.5*LIMIT) break;
                    totalCount++;  // Increment total count

                    var songObject = ConvertSongSearchObject(track);
                    
                    if (songObject == null) continue;   
                    notNullCount++;  // Increment count of non-null tracks found

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

            if (artistCsv.Length == 0 || string.IsNullOrEmpty(track.Album.Name))
            {
                return null;
            }

            return new SongSearchObject
            {
                Source = "spotify",
                Title = track.Name,
                Artists = artistCsv,
                ImageLocation = track.Album.Images[1].Url, // Medium image, 300x300
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

        public static SongSearchObject? ConvertSongSearchObject(SimpleTrack track, FullAlbum album)
        {
            var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
            if (artistCsv.Length == 0 || string.IsNullOrEmpty(track.Name))
            {
                return null;
            }
            return new SongSearchObject
            {
                Source = "spotify",
                Title = track.Name,
                Artists = artistCsv,
                ImageLocation = album.Images[1].Url, // Medium image, 300 x 300
                Id = track.Id,
                Duration = ((int)Math.Round(track.DurationMs / 1000.0)).ToString(),
                ReleaseDate = "", // No release date available in SimpleTrack
                AlbumName = album?.Name ?? "",
                Explicit = track.Explicit,
                TrackPosition = track.TrackNumber.ToString(),
            };
        }

        public static AlbumSearchObject? ConvertAlbumSearchObject(FullAlbum album)
        {
            var artistCsv = album.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
            if (artistCsv.Length == 0 || string.IsNullOrEmpty(album.Name))
            {
                return null;
            }

            return new AlbumSearchObject
            {
                Source = "spotify",
                Title = album.Name,
                Artists = artistCsv,
                ImageLocation = album.Images[1].Url, // medium image, 300 x 300
                Id = album.Id,
                Duration = ((int)Math.Round(album.Tracks.Items?.Sum(t => t.DurationMs / 1000.0) ?? 0)).ToString(),
                ReleaseDate = album.ReleaseDate,
                AlbumName = album.Name,
                Explicit = album.Tracks.Items?.Any(t => t.Explicit) ?? false,
                TrackPosition = "1",
                Rank = album.Popularity.ToString(),
                Isrc = album.ExternalIds.TryGetValue("upc", out var upc) ? upc : null,
                TracksCount = album.TotalTracks,
                TrackList = album.Tracks.Items?.Select(t => ConvertSongSearchObject(t, album))?.Where(t => t != null)?.ToList() ?? [],
                AdditionalFields = new Dictionary<string, object?> { { "artists", album.Artists}, { "cover_max", album.Images.First().Url} }
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

        public static async Task<string?> DownloadEquivalentTrack(string filePath, SongSearchObject song, bool strict = true, ConversionUpdateCallback? callback = default)
        {
            if (!IsInitialized)
            {
                return null;
            }
            // Remove extension if it exists
            filePath = ApiHelper.RemoveExtension(filePath);
            // 0 - mp3, 1 - flac
            var settingIdx = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.SpotifyQuality);

            var deezerEquivalent = await DeezerApi.GetDeezerTrack(song, onlyISRC: strict); // Try Deezer first

            if (deezerEquivalent != null) // Found on Deezer
            {
                var bitrateEnum = settingIdx switch
                {
                    0 => DeezNET.Data.Bitrate.MP3_320,
                    _ => DeezNET.Data.Bitrate.FLAC,
                };

                try // Wrap in try catch because deezer can throw exception (overwrite or api exception)
                {
                    var resultPath = await DeezerApi.DownloadTrack(filePath, deezerEquivalent, bitrateEnum);
                    callback?.Invoke(InfoBarSeverity.Success, song, resultPath);
                    return resultPath;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

            // Not found on Deezer, try Qobuz
            var qobuzEquivalent = await QobuzApi.GetQobuzTrack(song, onlyISRC: strict);

            if (qobuzEquivalent != null) // Found on Qobuz
            {
                try
                {
                    var resultPath = await QobuzApi.DownloadTrack(filePath, qobuzEquivalent, settingIdx == 0 ? "5" : " 6"); // mp3 or 16/44.1 flac
                    callback?.Invoke(InfoBarSeverity.Success, song, resultPath);
                    return resultPath;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

            // Reattempt deezer with 128 kbps fallback
            if (deezerEquivalent != null) // Found on Deezer
            {
                var bitrateEnum = settingIdx switch
                {
                    0 => DeezNET.Data.Bitrate.MP3_128,
                    _ => DeezNET.Data.Bitrate.FLAC,
                };

                try // Wrap in try catch because deezer can throw exception (overwrite or api exception)
                {
                    var resultPath = await DeezerApi.DownloadTrack(filePath, deezerEquivalent, bitrateEnum, use128Fallback: true);
                    callback?.Invoke(InfoBarSeverity.Success, song, resultPath);
                    return resultPath;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

            if (strict) // If strict, do not try youtube
            {
                return null;
            }

            var equivalent = await YoutubeApi.GetYoutubeTrack(song);

            if (equivalent != null) // Found on Youtube
            {
                try
                {
                    var opusLocation = ApiHelper.RemoveExtension(filePath) + ".opus";

                    if (File.Exists(opusLocation) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false) // If file exists and overwrite is false
                    {
                        return null;
                    }

                    if (!FFmpegRunner.IsInitialized)
                    {
                        return null;
                    }

                    await YoutubeApi.DownloadAudio(opusLocation, equivalent.Id); // Download audio as opus

                    string? convertedLocation = "";

                    if (settingIdx == 1)
                    {
                        await FFmpegRunner.ConvertToFlacAsync(opusLocation); // Convert opus to flac
                        convertedLocation = opusLocation.Replace(".opus", ".flac");
                    }
                    else
                    {
                        convertedLocation = await FFmpegRunner.CreateMp3Async(opusLocation);
                        await Task.Run(() => File.Delete(opusLocation));
                    }
                    callback?.Invoke(InfoBarSeverity.Warning, song, convertedLocation); // Not perfect match
                    return convertedLocation; // Return the converted location
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

            return null;
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
