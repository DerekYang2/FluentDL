using CommunityToolkit.WinUI.UI.Animations;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using QobuzApiSharp.Models.Content;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models;
using YouTubeMusicAPI.Models.Info;
using YouTubeMusicAPI.Models.Search;
using static System.Net.WebRequestMethods;

namespace FluentDL.Services
{
    internal class YoutubeApi
    {
        private static YoutubeClient youtube = new YoutubeClient();
        private static YouTubeMusicClient ytm = new YouTubeMusicClient();

        public YoutubeApi()
        {
        }

        public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25, bool albumMode = false)
        {
            query = query.Trim(); // Trim the query
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }
            int ct = 0; // Count of results
            itemSource.Clear();

            if (albumMode)
            {
                try
                {
                    await foreach (var album in ytm.SearchAsync(query, SearchCategory.Albums))
                    {
                        if (token.IsCancellationRequested || ct >= limit)
                            break;
                        var browseId = await ytm.GetAlbumBrowseIdAsync(album.Id); // Get the browse id
                        itemSource.Add(ConvertAlbumSearchObject(await ytm.GetAlbumInfoAsync(browseId)));
                        ct++;
                    }
                } catch (Exception e)
                {
                    Debug.WriteLine("Failed to search for albums: " + e.ToString());
                }
            }
            else
            {
                try
                {
                    await foreach (var song in ytm.SearchAsync(query, SearchCategory.Songs))
                    {
                        if (token.IsCancellationRequested || ct >= limit)
                            break;
                        var track = await GetTrack(song.Id);
                        if (track != null)
                        {
                            itemSource.Add(track);
                            ct++;
                        }
                    }
                }
                catch (Exception e1)
                {
                    Debug.WriteLine("Error searching YTM: " + e1.Message);

                    // Fall back to regular youtube
                    try
                    {
                        await foreach (var result in youtube.Search.GetVideosAsync(query, token))
                        {
                            if (token.IsCancellationRequested || ct >= limit)
                                break;

                            var songObj = await GetTrack(result.Id);

                            if (songObj != null)
                            {
                                itemSource.Add(songObj);
                                ct++;
                            }
                        }
                    }
                    catch (Exception e2)
                    {
                        Debug.WriteLine("Error searching YouTube: " + e2.Message);
                    }
                    return;
                }
            }
        }

        private static bool CloseMatch(string str1, string str2)
        {
            return ApiHelper.IsSubstring(str1.ToLower(), str2.ToLower());
        }

        public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token = default, int limit = 25)
        {
            // Youtube doesn't have an advanced search, must be done manually
            if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(albumName))
            {
                return;
            }

            artistName = ApiHelper.EnforceAscii(artistName.Trim());
            trackName = ApiHelper.EnforceAscii(trackName.Trim());
            albumName = ApiHelper.EnforceAscii(albumName.Trim());

            itemSource.Clear();

            bool isArtistSpecified = !string.IsNullOrWhiteSpace(artistName);
            bool isTrackSpecified = !string.IsNullOrWhiteSpace(trackName);

            var trackIdList = new HashSet<string>();

            if (!string.IsNullOrWhiteSpace(albumName)) // Album was specified
            {
                // Add any album matches
                await foreach (var searchResult in ytm.SearchAsync(albumName, SearchCategory.Albums))
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled
                    var browseId = await ytm.GetAlbumBrowseIdAsync(searchResult.Id);
                    var album = await ytm.GetAlbumInfoAsync(browseId);

                    // Ensure album artist matches to quickly filter out results
                    var oneArtistMatch = false;
                    if (isArtistSpecified) // If artist is specified
                    {
                        foreach (var queryArtist in album.Artists)
                        {
                            if (CloseMatch(queryArtist.Name, artistName))
                            {
                                oneArtistMatch = true;
                                break;
                            }
                        }
                    }
                    else // If artist is not specified
                    {
                        oneArtistMatch = true; // Assume true if artist not specified
                    }

                    if (oneArtistMatch && CloseMatch(album.Name, albumName)) // This album result substring matches
                    {
                        // https://music.youtube.com/playlist?list={albumId}

                        var playlistUrl = $"https://music.youtube.com/playlist?list={album.Id}";

                        try
                        {
                            await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(playlistUrl, token))
                            {
                                if (token.IsCancellationRequested)
                                {
                                    break;
                                }

                                var song = await ytm.GetSongVideoInfoAsync(playlistVideo.Id);

                                bool valid = true; // True as default

                                if (isArtistSpecified) // Album and artist specified
                                {
                                    if (isTrackSpecified) // Album, artist, track specified
                                    {
                                        oneArtistMatch = false;
                                        foreach (var artist in song.Artists) // Check if at least one artist matches
                                        {
                                            if (CloseMatch(artist.Name, artistName))
                                            {
                                                oneArtistMatch = true;
                                                break;
                                            }
                                        }

                                        valid = oneArtistMatch && CloseMatch(trackName, song.Name);
                                    }
                                }
                                else if (isTrackSpecified) // Album and track specified
                                {
                                    if (isTrackSpecified) // Album and track specified
                                    {
                                        valid = CloseMatch(trackName, song.Name);
                                    }
                                }

                                if (valid && !trackIdList.Contains(song.Id)) // If valid and not already added
                                {
                                    itemSource.Add(await ConvertSongSearchObject(song));
                                    trackIdList.Add(song.Id);
                                    if (trackIdList.Count >= limit) break;
                                }
                            }
                        }
                        catch (Exception e) // Attempt other (worse) method if playlist method fails
                        {
                            // YTM api method, worse because uses videos rather than songs:
                            var albumInfo = await ytm.GetAlbumInfoAsync(await ytm.GetAlbumBrowseIdAsync(album.Id)); // Get full album info

                            foreach (var albumSongInfo in albumInfo.Songs) // Iterate through album tracks
                            {
                                if (token.IsCancellationRequested) return; // Check if task is cancelled
                                var song = await ytm.GetSongVideoInfoAsync(albumSongInfo.Id); // Get full track info

                                bool valid = true; // True as default

                                if (isArtistSpecified) // Album and artist specified
                                {
                                    if (isTrackSpecified) // Album, artist, track specified
                                    {
                                        oneArtistMatch = false;
                                        foreach (var artist in song.Artists) // Check if at least one artist matches
                                        {
                                            if (CloseMatch(artist.Name, artistName))
                                            {
                                                oneArtistMatch = true;
                                                break;
                                            }
                                        }

                                        valid = oneArtistMatch && CloseMatch(trackName, song.Name);
                                    }
                                }
                                else if (isTrackSpecified) // Album and track specified
                                {
                                    if (isTrackSpecified) // Album and track specified
                                    {
                                        valid = CloseMatch(trackName, song.Name);
                                    }
                                }

                                if (valid && !trackIdList.Contains(song.Id)) // If valid and not already added
                                {
                                    itemSource.Add(await ConvertSongSearchObject(song));
                                    trackIdList.Add(song.Id);
                                    if (trackIdList.Count >= limit) break;
                                }
                            }
                        }
                    }

                    if (trackIdList.Count >= limit) break;
                }
            }
            else // No album specified
            {
                if (isTrackSpecified) // If artist and track are specified
                {
                    var query = artistName + " " + trackName;

                    await foreach (var result in ytm.SearchAsync(query, SearchCategory.Songs))
                    {
                        if (token.IsCancellationRequested || trackIdList.Count >= limit) return; // Check if task is cancelled or limit reached
                        var song = await ytm.GetSongVideoInfoAsync(result.Id);

                        bool oneArtistMatch = false;
                        foreach (var artist in song.Artists) // Check if at least one artist matches
                        {
                            if (CloseMatch(artist.Name, artistName))
                            {
                                oneArtistMatch = true;
                                break;
                            }
                        }

                        if (oneArtistMatch && CloseMatch(song.Name, trackName)) // If at least one artist match and track name close match
                        {
                            if (!trackIdList.Contains(song.Id)) // If not already added
                            {
                                itemSource.Add(await ConvertSongSearchObject(song));
                                trackIdList.Add(song.Id);
                            }
                        }
                    }
                }
                else // If only artist is specified
                { 
                    try {
                        await foreach (var artistResult in ytm.SearchAsync(artistName, SearchCategory.Artists))
                        {
                            if (token.IsCancellationRequested || trackIdList.Count >= limit) return; // Check if task is cancelled or limit reached

                            var artistInfo = await ytm.GetArtistInfoAsync(artistResult.Id);

                            foreach (var song in artistInfo.Songs) // Iterate through artist tracks
                            {
                                if (token.IsCancellationRequested || trackIdList.Count >= limit) return; // Check if task is cancelled or limit reached

                                bool oneArtistMatch = false;
                                foreach (var artist in song.Artists) // Check if at least one artist matches
                                {
                                    if (CloseMatch(artist.Name, artistName))
                                    {
                                        oneArtistMatch = true;
                                        break;
                                    }
                                }

                                if (oneArtistMatch && !trackIdList.Contains(song.Id)) // If not already added
                                {
                                    var songObj = await GetTrack(song.Id);
                                    if (songObj != null)
                                    {
                                        itemSource.Add(songObj);
                                        trackIdList.Add(song.Id);
                                    }
                                }
                            }
                        }
                    } catch (Exception e)
                    {
                        Debug.WriteLine("YTM: error searching for artist: " + e.Message);
                    }
                }
            }
        }

        public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
        {
            if (url.StartsWith("https://www.youtube.com/watch?") || url.StartsWith("https://youtube.com/watch?")) // Youtube video
            {
                var video = await youtube.Videos.GetAsync(url);
                var songObj = await ConvertSongSearchObject(video);
                if (songObj == null)  // Some error occurred
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>YouTube</b>   Error loading track from <a href='{url}'>{url}</a>");
                    return;
                }

                itemSource.Add(songObj);

                if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>YouTube</b>   Loaded track <a href='{url}'>{songObj.Title}</a>");
                }
                else // Video
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>YouTube</b>   Loaded video <a href='{url}'>{songObj.Title}</a>");
                }
            }

            if (url.StartsWith("https://music.youtube.com/watch?v=")) // Youtube music has both songs and videos
            {
                var video = await youtube.Videos.GetAsync(url);
                var songObj = await ConvertSongSearchObject(video);
                if (songObj == null)  // Some error occurred
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>YouTube Music</b>   Error loading track from <a href='{url}'>{url}</a>");
                    return;
                }

                itemSource.Add(songObj);

                if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>YouTube Music</b>   Loaded track <a href='{url}'>{songObj.Title}</a>");
                }
                else // Video
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>YouTube Music</b>   Loaded video <a href='{url}'>{songObj.Title}</a>");
                }
            }

            if (url.StartsWith("https://www.youtube.com/playlist?") || url.StartsWith("https://youtube.com/playlist?") || url.StartsWith("https://music.youtube.com/playlist?"))
            {
                // remove &si= and everything after in the url if it exists
                url = url.Split("&si=")[0];
                var playlistObj = await youtube.Playlists.GetAsync(url);
                var playlistName = playlistObj.Title;

                // If load album object
                if (albumMode)
                {
                    try
                    {
                        var browseId = await ytm.GetAlbumBrowseIdAsync(playlistObj.Id);  // Can throw exception if playlist url instead of album
                        itemSource.Add(ConvertAlbumSearchObject(await ytm.GetAlbumInfoAsync(browseId)));
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error loading album: " + ex.ToString());
                    }
                }

                // Show a permanent, loading message
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>YouTube</b>   Loading playlist <a href='{url}'>{playlistName}</a>", -1);

                itemSource.Clear(); // Clear the item source

                var failCount = 0;
                try
                {
                    await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(url, token))
                    {
                        if (token.IsCancellationRequested)
                        {
                            statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>YouTube</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>");
                            return;
                        }

                        var video = await youtube.Videos.GetAsync(playlistVideo.Id); // Get the full video object
                        var song = await ConvertSongSearchObject(video); // Convert to song object
                        if (song != null) // If not null
                        {
                            itemSource.Add(song); // Add to the item source
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (token.IsCancellationRequested) // Can crash on cancel
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>YouTube</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>");
                        return;
                    }

                    Debug.WriteLine("Error loading playlist: " + e.Message);
                    statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>YouTube</b>   Error loading playlist <a href='{url}'>{playlistName}</a>");
                    return;
                }

                // Replace the loading message with a result message
                if (failCount > 0) // If some failed
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>YouTube</b>   Loaded playlist <a href='{url}'>{playlistName}</a> with {failCount} failed {(failCount == 1 ? "track" : "tracks")}");
                }
                else // All loaded successfully
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>YouTube</b>   Loaded playlist <a href='{url}'>{playlistName}</a>");
                }
            }
        }


        public static async Task<YoutubeExplode.Search.VideoSearchResult?> GetSearchResult(SongSearchObject song)
        {
            // Convert artists csv to array
            var artists = song.Artists.Split(", ");
            for (int i = 0; i < artists.Length; i++)
            {
                artists[i] = artists[i].ToLower();
            }

            var query = artists[0] + " " + song.Title; // TODO: maybe try other artists or even all if fail?
            var ct = 0;

            var results = new List<YoutubeExplode.Search.VideoSearchResult>();

            await foreach (var result in youtube.Search.GetVideosAsync(query))
            {
                var id = result.Id;
                var author = result.Author;
                var title = result.Title;
                var duration = result.Duration;
                double totalSeconds = TimeSpan.Parse(duration?.ToString() ?? "0").TotalSeconds;

                if (Math.Abs(totalSeconds - double.Parse(song.Duration)) <= 1)
                {
                    results.Add(result);
                }

                if (results.Count > 0 && (++ct >= 25 || results.Count >= 5)) // If total results is 10 or more or 5 results are found
                {
                    break;
                }
            }

            // 1: if author contains artist name and title matches 
            // 2: if author contains artist name
            // 3: if title contains song title and artist name and has "audio" in title
            // 4: if title contains song title and artist name
            // 5: if title contains song title and has "audio" in title
            // 6: if title contains song title
            // 7: take the first result

            // Pass 1
            foreach (var result in results)
            {
                var videoAuthor = result.Author.ChannelTitle.ToLower();
                var pruneTitle = DeezerApi.PruneTitle(result.Title);
                var songTitle = DeezerApi.PruneTitle(song.Title);

                if (pruneTitle.Contains(songTitle))
                {
                    foreach (var artistName in artists)
                    {
                        if (videoAuthor.Contains(artistName))
                        {
                            return result;
                        }
                    }
                }
            }


            // Pass 2
            foreach (var result in results)
            {
                var videoAuthor = result.Author.ChannelTitle.ToLower();
                foreach (var artistName in artists)
                {
                    if (videoAuthor.Contains(artistName))
                    {
                        return result;
                    }
                }
            }

            // Pass 3
            foreach (var result in results)
            {
                var pruneTitle = DeezerApi.PruneTitle(result.Title);
                var songTitle = DeezerApi.PruneTitle(song.Title);

                bool containsOneArtist = false;
                foreach (var artist in artists)
                {
                    if (result.Title.ToLower().Contains(artist)) // If title contains at least one artist
                    {
                        containsOneArtist = true;
                        break;
                    }
                }

                if (pruneTitle.Contains(songTitle) && containsOneArtist && result.Title.ToLower().Contains("audio"))
                {
                    return result;
                }
            }

            // Pass 4
            foreach (var result in results)
            {
                var pruneTitle = DeezerApi.PruneTitle(result.Title);
                var songTitle = DeezerApi.PruneTitle(song.Title);

                bool containsOneArtist = false;
                foreach (var artist in artists)
                {
                    if (result.Title.ToLower().Contains(artist)) // If title contains at least one artist
                    {
                        containsOneArtist = true;
                        break;
                    }
                }

                if (pruneTitle.Contains(songTitle) && containsOneArtist)
                {
                    return result;
                }
            }

            // Pass 5
            foreach (var result in results)
            {
                var pruneTitle = DeezerApi.PruneTitle(result.Title);
                var songTitle = DeezerApi.PruneTitle(song.Title);
                if (pruneTitle.Contains(songTitle) && result.Title.ToLower().Contains("audio"))
                {
                    return result;
                }
            }


            // Pass 6
            foreach (var result in results)
            {
                var pruneTitle = DeezerApi.PruneTitle(result.Title);
                var songTitle = DeezerApi.PruneTitle(song.Title);
                if (pruneTitle.Contains(songTitle))
                {
                    return result;
                }
            }

            // Pass 7
            if (results.Count > 0)
            {
                return results[0];
            }

            // If no results found
            return null;
        }

        public static async Task<SongSearchObject?> GetYoutubeTrack(SongSearchObject songObj, CancellationToken token = default, ConversionUpdateCallback? callback = null)
        {
            // Convert artists csv to array
            var artists = songObj.Artists.Split(", ");
            for (int i = 0; i < artists.Length; i++)
            {
                artists[i] = artists[i].ToLower();
            }

            var artistName = songObj.Artists.Split(", ")[0]; // Get one artist
            var trackName = songObj.Title;
            var albumName = songObj.AlbumName;

            int ct = 0;
            try
            {
                // Start searching through albums
                await foreach (var result in ytm.SearchAsync(albumName, SearchCategory.Albums))
                {
                    if (token.IsCancellationRequested) return null; // Check if task is cancelled
                    if (ct++ >= 15) break;

                    var browseId = await ytm.GetAlbumBrowseIdAsync(result.Id); // Get the browse id
                    var album = await ytm.GetAlbumInfoAsync(browseId);

                    // Ensure album artist matches
                    bool oneArtistMatch = false;
                    foreach (var queryArtist in album.Artists)
                    {
                        foreach (var artist in artists)
                        {
                            if (CloseMatch(queryArtist.Name, artist))
                            {
                                oneArtistMatch = true;
                                break;
                            }
                        }
                    }

                    if (oneArtistMatch && CloseMatch(album.Name, albumName)) // This album result substring matches
                    {
                        var playlistUrl = $"https://music.youtube.com/playlist?list={album.Id}";

                        try
                        {
                            await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(playlistUrl, token))
                            {
                                var song = await ytm.GetSongVideoInfoAsync(playlistVideo.Id, token);
                                if (token.IsCancellationRequested)
                                {
                                    return null;
                                }

                                oneArtistMatch = false;
                                foreach (var queryArtist in song.Artists) // Check if at least one artist matches for track
                                {
                                    foreach (var artist in artists)
                                    {
                                        if (CloseMatch(queryArtist.Name, artist))
                                        {
                                            oneArtistMatch = true;
                                            break;
                                        }
                                    }
                                }

                                if (oneArtistMatch && CloseMatch(song.Name, trackName))
                                {
                                    var retObj = await ConvertSongSearchObject(song);
                                    if (retObj != null)
                                    {
                                        callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                                        return retObj;
                                    }
                                }
                            }
                        }
                        catch (Exception e) // Can crash on invalid playlist or token cancel (for some reason)
                        {
                            Debug.WriteLine("Error loading playlist: " + e.Message);
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error searching for album: " + e.ToString());
            }

            // Try searching without album
            var query = artistName + " " + trackName;

            try
            {
                await foreach (var res in ytm.SearchAsync(query, SearchCategory.Songs))
                {
                    if (token.IsCancellationRequested) return null; // Check if task is cancelled
                    if (ApiHelper.PrunePunctuation(songObj.Title.ToLower()).Equals(ApiHelper.PrunePunctuation(res.Name.ToLower())))
                    {
                        foreach (var artist in artists)
                        {
                            var fullRes = await ytm.GetSongVideoInfoAsync(res.Id);
                            foreach (var artistResult in fullRes.Artists)
                            {
                                if (CloseMatch(artistResult.Name, artist))
                                {
                                    var retObj = await ConvertSongSearchObject(fullRes);
                                    callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                                    return retObj;
                                }
                            }
                        }
                    }
                }
            } catch (Exception e)
            {
                Debug.WriteLine("Error searching without album: " + e.ToString());
            }

            // Fall back to searching for videos
            var searchResult = await GetSearchResult(songObj);
            if (searchResult == null)
            {
                callback?.Invoke(InfoBarSeverity.Error, songObj);  // Show error badge with original object
                return null;
            }

            var ret = await GetTrack(searchResult.Id);
            if (ret == null) // If not found
            {
                callback?.Invoke(InfoBarSeverity.Error, songObj);  // Show error badge with original object
                return null;
            }
            callback?.Invoke(InfoBarSeverity.Warning, ret); // Not found by ISRC
            return ret;
        }

        // Largest image ends in maxresdefault.webp
        // Use mqdefault.jpg for smaller image (no black borders)
        /*
            Image formats:
            https://i.ytimg.com/vi_webp/{id}/maxresdefault.webp
            OR
            https://i.ytimg.com/vi/{id}/maxresdefault.jpg
            https://i.ytimg.com/vi/{id}/maxresdefault.jpg?{string}

            https://img.youtube.com/vi/{id}/default.jpg
            https://img.youtube.com/vi/{id}/mqdefault.jpg
            https://img.youtube.com/vi/{id}/hqdefault.jpg

            Youtube music image format:
            https://lh3.googleusercontent.com/{string}=w60-h60-l90-rj
            https://lh3.googleusercontent.com/{string}=w120-h120-l90-rj
            https://lh3.googleusercontent.com/{string}=w544-h544-l90-rj (not returned by youtube music api, manually get)
         */
        public static async Task<string?> GetMaxResThumbnail(SongSearchObject song)
        {
            if (song.ImageLocation == null)
                return null;

            if (song.ImageLocation.StartsWith("https://lh3.googleusercontent.com")) // If youtube music image
            {
                string imageUrl = song.ImageLocation;
                // Delete everything after the last =
                var i = imageUrl.LastIndexOf("=");
                imageUrl = imageUrl.Substring(0, i + 1); // Include everything up to and including the =
                imageUrl += "w544-h544-l90-rj"; // Add the max res version
                return imageUrl;
            }

            var video = await youtube.Videos.GetAsync(song.Id);
            if (video.Thumbnails.Count == 0) 
                return null;

            var idx = video.Thumbnails.ToList().FindIndex(x =>
            {
                var url = x.Url;
                // Remove query string
                var i = url.LastIndexOf('?');
                if (i != -1) url = url.Substring(0, i);
                return url.EndsWith("maxresdefault.webp") || url.EndsWith("maxresdefault.jpg");
            });

            if (idx == -1) // If not found
            {
                idx = 0; // Just take first
            }

            return video.Thumbnails[idx].Url;
        }

        public static string GetMaxResThumbnail(SongVideoInfo? ytmSong, YoutubeExplode.Videos.Video video)
        {
            if (ytmSong !=null && ytmSong.Thumbnails.Length > 0 && 
                video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
            {
                var imageUrl = ytmSong.Thumbnails.Last().Url; // Use second youtube music image (larger, 120px)
                if (imageUrl.StartsWith("https://lh3.googleusercontent.com"))
                {
                    // Delete everything after the last =
                    var i = imageUrl.LastIndexOf("=");
                    imageUrl = imageUrl.Substring(0, i + 1); // Include everything up to and including the =
                    imageUrl += "w544-h544-l90-rj"; // Add the max res version
                    return imageUrl;
                }
            }

            // Normal video
            var idx = video.Thumbnails.ToList().FindIndex(x =>
            {
                var url = x.Url;
                // Remove query string
                var i = url.LastIndexOf('?');
                if (i != -1) url = url.Substring(0, i);
                return url.EndsWith("maxresdefault.webp") || url.EndsWith("maxresdefault.jpg");
            });

            if (idx == -1) // If not found
            {
                idx = 0; // Just take first
            }

            return video.Thumbnails[idx].Url;
        }

        public static async Task<SongSearchObject> GetTrack(SongSearchResult ytmSong)
        {
            var artistString = string.Join(", ", ytmSong.Artists.Where(a => a.Id != null).Select(a => a.Name).ToList());

            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views

            return new SongSearchObject()
            {
                AlbumName = ytmSong.Album.Name,
                Artists = artistString,
                Duration = ytmSong.Duration.TotalSeconds.ToString(),
                Source = "youtube",
                Explicit = ytmSong.IsExplicit,
                Id = ytmSong.Id,
                ImageLocation = ytmSong.Thumbnails.Last().Url,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = FormatLargeValue(video.Engagement.ViewCount),
                TrackPosition = "1",
                ReleaseDate = ApiHelper.FormatDateTimeOffset(video.UploadDate),
                Title = ytmSong.Name,
            };
        }

        public static async Task<SongSearchObject?> GetTrack(string id)
        {
            var video = await youtube.Videos.GetAsync(id);
            return await ConvertSongSearchObject(video);
        }

        public static SongSearchObject GetTrack(SongVideoInfo? ytmSong, YoutubeExplode.Videos.Video video)
        {
            // Get artist csv
            string artistString;
            
            if (ytmSong == null)
            {
                artistString = video.Author.ChannelTitle; // Use video author as artist if ytmSong is null
            }
            else
            {
                artistString = string.Join(", ", ytmSong.Artists.Where(a => a.Id != null).Select(a => a.Name).ToList());
            }

            // These values vary between song or video
            string albumName;
            string imageLocation;

            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
            {
                var lines = video.Description.Split("\n");
                albumName = lines[4].Trim(); // Fourth line of ytm description contains album name
            }
            else // Normal video
            {
                albumName = video.Title; // Use video title as album name
            }

            // Prefer ytm album image
            if (ytmSong != null && ytmSong.Thumbnails.Length > 0)
            {
                imageLocation = ytmSong.Thumbnails.Last().Url; // Use second youtube music image (larger, 120px)
            }
            else 
            {
                var idx = video.Thumbnails.ToList().FindIndex(x =>
                {
                    var url = x.Url;
                    // Remove query string
                    var i = url.LastIndexOf('?');
                    if (i != -1) url = url.Substring(0, i);
                    return url.EndsWith("mqdefault.webp") || url.EndsWith("mqdefault.jpg"); // Use mqdefault thumbnail
                });

                if (idx == -1) // If not found
                {
                    idx = 0; // Just take first
                }

                imageLocation = video.Thumbnails[idx].Url; // Use max res thumbnail
            }

            return new SongSearchObject()
            {
                AlbumName = albumName,
                Artists = artistString,
                Duration = ytmSong?.Duration.TotalSeconds.ToString() ?? video.Duration?.TotalSeconds.ToString() ?? "0",
                Source = "youtube",
                Explicit = !ytmSong?.IsFamiliyFriendly ?? false,
                Id = ytmSong?.Id ?? video.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = FormatLargeValue(video.Engagement.ViewCount),
                TrackPosition = "1",
                ReleaseDate = ApiHelper.FormatDateTimeOffset(video.UploadDate),
                Title = ytmSong?.Name ?? video.Title,
            };
        }

        public static async Task<SongSearchObject> ConvertSongSearchObject(YoutubeExplode.Videos.Video video)
        {
            SongVideoInfo? ytmInfo = null;
            try
            {
                ytmInfo = await ytm.GetSongVideoInfoAsync(video.Id);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error converting song search object: " + e.Message);
            }
            return GetTrack(ytmInfo, video);
        }

        public static async Task<SongSearchObject> ConvertSongSearchObject(SongVideoInfo ytmSong)
        {
            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views
            return GetTrack(ytmSong, video);
        }

        public static AlbumSearchObject ConvertAlbumSearchObject(AlbumInfo ytmAlbum) {
            var artistString = string.Join(',', ytmAlbum.Artists.Where(a => a.Id != null).Select(a => a.Name).ToList());

            string imageLocation = "";
            if (ytmAlbum.Thumbnails.Length > 0)
            {
                imageLocation = ytmAlbum.Thumbnails.Last().Url; // Use second youtube music image (larger, 120px)
            }

            return new AlbumSearchObject()
            {
                AlbumName = ytmAlbum.Name,
                Artists = artistString,
                Duration = ytmAlbum.Duration.TotalSeconds.ToString() ?? "0",
                Source = "youtube",
                Explicit = false,
                Id = ytmAlbum.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = "",
                TrackPosition = "1",
                ReleaseDate = $"{ytmAlbum.ReleaseYear}-01-01",
                Title = ytmAlbum.Name,
                TracksCount = ytmAlbum.SongCount,
                TrackList = ytmAlbum.Songs.Select(song => ConvertSongSearchObject(song, ytmAlbum)).ToList()
            };
        }

        public static SongSearchObject ConvertSongSearchObject(AlbumSong song, AlbumInfo album)
        {
            var artistString = string.Join(", ", album.Artists.Where(a => a.Id != null).Select(a => a.Name).ToList());

            string imageLocation = "";
            if (album.Thumbnails.Length > 0)
            {
                imageLocation = album.Thumbnails.Last().Url; // Use second youtube music image (larger, 120px)
            }

            return new SongSearchObject()
            {
                AlbumName = album.Name,
                Artists = artistString,
                Duration = song.Duration.TotalSeconds.ToString() ?? "0",
                Source = "youtube",
                Explicit = song.IsExplicit,
                Id = song.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = song.PlaysInfo ?? "",
                TrackPosition = song.SongNumber?.ToString() ?? "1",
                ReleaseDate = $"{album.ReleaseYear}-01-01",
                Title = song.Name,
            };
        }

        /*
         Old code to parse fields from description, not fully needed anymore because ytm api:
            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Youtube music
           {
               // Format is: "Song title" · Artist 1 · Artist 2 · etc

               var lines = video.Description.Split("\n");


               var fields = lines[2].Trim().Split(" · "); // third line

               title = fields[0];

               var artistsBuilder = new StringBuilder(); // Artists CSV
               for (int i = 1; i < fields.Length; i++)
               {
                   artistsBuilder.Append(fields[i]);
                   if (i < fields.Length - 1)
                   {
                       artistsBuilder.Append(", ");
                   }
               }

               artists = artistsBuilder.ToString();

               albumName = lines[4].Trim(); // Fourth line of ytm description contains album name
           }
         */

        // Billion -> B, Million -> M, Thousand -> K
        public static string FormatLargeValue(long value)
        {
            if (value >= 1e9) // Round two decimal places
            {
                return string.Format("{0:F2}B", value / 1e9);
            }

            if (value >= 1e6)
            {
                return string.Format("{0:F1}M", value / 1e6);
            }

            if (value >= 1e3)
            {
                return string.Format("{0:F0}K", value / 1e3);
            }

            return value.ToString();
        }


        public static async Task DownloadAudio(string filePath, VideoId id)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(id);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            string extension = streamInfo.Container.ToString();

            long maxBitRate = 0;
            foreach (var streamObj in streamManifest.GetAudioStreams()) // Get the opus stream with highest bitrate
            {
                if (streamObj.AudioCodec.Equals("opus") && streamObj.Bitrate.BitsPerSecond > maxBitRate)
                {
                    extension = streamObj.AudioCodec;
                    maxBitRate = streamObj.Bitrate.BitsPerSecond;
                    streamInfo = streamObj;
                }
            }

            await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath /*, new Progress<double>(progressHandler)*/);
        }

        /*
           Example codecs:
           Codec: mp4a.40.2 145.77 Kbit/s
           Codec: mp4a.40.5 50.11 Kbit/s
           Codec: mp4a.40.2 128.69 Kbit/s
           Codec: opus 65.11 Kbit/s
           Codec: opus 80.41 Kbit/s
           Codec: opus 134.56 Kbit/s
         */
        public static async Task DownloadAudioAAC(string filePath, VideoId id)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(id);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            long maxBitRate = 0;
            foreach (var streamObj in streamManifest.GetAudioOnlyStreams()) // Get the aac stream with highest bitrate
            {
                if (streamObj.AudioCodec.Contains("mp4a") && streamObj.Bitrate.BitsPerSecond > maxBitRate)
                {
                    maxBitRate = streamObj.Bitrate.BitsPerSecond;
                    streamInfo = streamObj;
                }
            }

            await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath /*, new Progress<double>(progressHandler)*/);
        }

        public static async Task UpdateMetadata(string filePath, string id)
        {
            var video = await youtube.Videos.GetAsync(id);
            var ytmSong = await ytm.GetSongVideoInfoAsync(id);

            var artists = new List<string>();
            foreach (var artist in ytmSong.Artists)
            {
                if (artist.Id == null) continue;
                artists.Add(artist.Name);
            }

            // These values vary between song or video
            string albumName;

            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
            {
                var lines = video.Description.Split("\n");
                albumName = lines[4].Trim(); // Fourth line of ytm description contains album name
            }
            else // Normal video
            {
                albumName = video.Title; // Use video title as album name
            }

            try
            {
                var metadata = new MetadataObject(filePath)
                {
                    Title = ytmSong.Name,
                    Artists = artists.ToArray(),
                    AlbumArtPath = GetMaxResThumbnail(ytmSong, video),
                    AlbumName = albumName,
                    AlbumArtists = new[] { artists.First() },
                    ReleaseDate = video.UploadDate.Date,
                    TrackNumber = 1,
                    TrackTotal = 1,
                    Url = video.Url,
                };

                await metadata.SaveAsync();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error updating metadata: " + e.Message);
            }

            //Track atlTrack = new Track(filePath) // For metadata
            //{
            //    Title = ytmSong.Name,
            //    Album = albumName,
            //    AlbumArtist = ytmSong.Artists.First().Name,
            //    Artist = artistCSV.ToString(),
            //    AudioSourceUrl = video.Url,
            //    AdditionalFields = { { "YEAR", video.UploadDate.Year.ToString() } },
            //    Date = video.UploadDate.Date,
            //    TrackNumber = 1,
            //    TrackTotal = 1,
            //    Popularity = video.Engagement.ViewCount,
            //};

            //// Get image bytes for cover art
            //var imageBytes = await new HttpClient().GetByteArrayAsync(GetMaxResThumbnail(ytmSong, video));
            //PictureInfo newPicture = PictureInfo.fromBinaryData(imageBytes, PictureInfo.PIC_TYPE.Front);

            //// Append to front if pictures already exist
            //if (atlTrack.EmbeddedPictures.Count > 0)
            //{
            //    atlTrack.EmbeddedPictures.Insert(0, newPicture);
            //}
            //else
            //{
            //    atlTrack.EmbeddedPictures.Add(newPicture);
            //}

            //await atlTrack.SaveAsync();
        }

        public static async Task<string> AudioStreamUrl(string url)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            long maxBitRate = 0;
            foreach (var streamObj in streamManifest.GetAudioStreams()) // Get the opus stream with highest bitrate
            {
                if (streamObj.AudioCodec.Equals("opus") && streamObj.Bitrate.BitsPerSecond > maxBitRate)
                {
                    maxBitRate = streamObj.Bitrate.BitsPerSecond;
                    streamInfo = streamObj;
                }
            }

            return streamInfo.Url;
        }

        // Get stream with minimum bitrate
        public static async Task<string> AudioStreamWorstUrl(string url)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            long minBitRate = long.MaxValue;
            foreach (var streamObj in streamManifest.GetAudioStreams()) // Get the opus stream with highest bitrate
            {
                if (streamObj.AudioCodec.Equals("opus") && streamObj.Bitrate.BitsPerSecond < minBitRate)
                {
                    minBitRate = streamObj.Bitrate.BitsPerSecond;
                    streamInfo = streamObj;
                }
            }

            return streamInfo.Url;
        }
    }
}