using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Protection.PlayReady;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models;
using YouTubeMusicAPI.Models.Info;
using static System.Net.WebRequestMethods;
using Video = YouTubeMusicAPI.Models.Video;

namespace FluentDL.Services
{
    internal class YoutubeApi
    {
        private static YoutubeClient youtube = new YoutubeClient();
        private static YouTubeMusicClient ytm = new YouTubeMusicClient();

        public YoutubeApi()
        {
        }

        public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25)
        {
            query = query.Trim(); // Trim the query
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            itemSource.Clear();

            var searchResults = await ytm.SearchAsync<Song>(query, token);

            foreach (var song in searchResults)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                itemSource.Add(await GetTrack(song));
            }

            // NORMAL YOUTUBE SEARCH:
            //await foreach (var result in youtube.Search.GetVideosAsync(query, token))
            //{
            //    if (token.IsCancellationRequested)
            //    {
            //        break;
            //    }

            //    itemSource.Add(await GetTrack(result.Id));
            //}
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
                var searchResults = await ytm.SearchAsync<Album>(albumName, token); // Search for album first

                if (token.IsCancellationRequested) return; // If cancelled

                // Add any album matches
                foreach (var album in searchResults)
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled


                    if (CloseMatch(album.Name, albumName)) // This album result substring matches
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
                                        bool oneArtistMatch = false;
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
                            var browseId = await ytm.GetAlbumBrowseIdAsync(album.Id); // Get browse id
                            var albumInfo = await ytm.GetAlbumInfoAsync(browseId); // Get full album info

                            foreach (var albumSongInfo in albumInfo.Songs) // Iterate through album tracks
                            {
                                if (token.IsCancellationRequested) return; // Check if task is cancelled
                                var song = await ytm.GetSongVideoInfoAsync(albumSongInfo.Id); // Get full track info

                                bool valid = true; // True as default

                                if (isArtistSpecified) // Album and artist specified
                                {
                                    if (isTrackSpecified) // Album, artist, track specified
                                    {
                                        bool oneArtistMatch = false;
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
                    var searchResults = await ytm.SearchAsync<Song>(query, token);
                    if (token.IsCancellationRequested) return; // If cancelled

                    foreach (var song in searchResults)
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

                        if (oneArtistMatch && CloseMatch(song.Name, trackName)) // If at least one artist match and track name close match
                        {
                            if (!trackIdList.Contains(song.Id)) // If not already added
                            {
                                itemSource.Add(await GetTrack(song));
                                trackIdList.Add(song.Id);
                            }
                        }
                    }
                }
                else // If only artist is specified
                {
                    var artistResults = await ytm.SearchAsync<Artist>(artistName, token);
                    if (token.IsCancellationRequested) return; // If cancelled

                    foreach (var artistResult in artistResults)
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
                                itemSource.Add(await GetTrack(song.Id));
                                trackIdList.Add(song.Id);
                            }
                        }
                    }
                }
            }
        }

        public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate)
        {
            if (url.StartsWith("https://www.youtube.com/watch?"))
            {
                var video = await youtube.Videos.GetAsync(url);
                itemSource.Add(await ConvertSongSearchObject(video));
                statusUpdate.Invoke(InfoBarSeverity.Success, $"Added video \"{video.Title}\"");
            }

            if (url.StartsWith("https://music.youtube.com/watch?v=")) // Youtube music has both songs and videos
            {
                var video = await youtube.Videos.GetAsync(url);
                itemSource.Add(await ConvertSongSearchObject(video));
                statusUpdate.Invoke(InfoBarSeverity.Success, $"Added video \"{video.Title}\"");
            }

            if (url.StartsWith("https://www.youtube.com/playlist?") || url.StartsWith("https://music.youtube.com/playlist?"))
            {
                var playlistName = (await youtube.Playlists.GetAsync(url)).Title;
                statusUpdate.Invoke(InfoBarSeverity.Informational, $"Adding videos from playlist \"{playlistName}\"");

                await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(url, token))
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var video = await youtube.Videos.GetAsync(playlistVideo.Id); // Get the full video object
                    itemSource.Add(await ConvertSongSearchObject(video));
                }
            }
        }


        public static async Task<VideoSearchResult> GetSearchResult(SongSearchObject song)
        {
            // Convert artists csv to array
            var artists = song.Artists.Split(", ");
            for (int i = 0; i < artists.Length; i++)
            {
                artists[i] = artists[i].ToLower();
            }

            var query = artists[0] + " " + song.Title; // TODO: maybe try other artists or even all if fail?
            var ct = 0;

            var results = new List<VideoSearchResult>();

            await foreach (var result in youtube.Search.GetVideosAsync(query))
            {
                var id = result.Id;
                var author = result.Author;
                var title = result.Title;
                var duration = result.Duration;
                double totalSeconds = TimeSpan.Parse(duration.ToString()).TotalSeconds;

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

        public static async Task<SongSearchObject?> GetYoutubeTrack(SongSearchObject songObj, CancellationToken token = default)
        {
            // Convert artists csv to array
            var artists = songObj.Artists.Split(", ");
            for (int i = 0; i < artists.Length; i++)
            {
                artists[i] = artists[i].ToLower();
            }

            var advancedResults = new ObservableCollection<SongSearchObject>();
            await AdvancedSearch(advancedResults, songObj.Artists, songObj.Title, songObj.AlbumName, token, 5);
            if (advancedResults.Count > 0)
            {
                // 1: if author contains artist name and title matches 
                // 2: if title matches
                // 3: first result

                // Pass 1
                foreach (var result in advancedResults)
                {
                    var authorsCSV = result.Artists.ToLower();
                    if (ApiHelper.PrunePunctuation(songObj.Title.ToLower()).Equals(ApiHelper.PrunePunctuation(result.Title.ToLower())))
                    {
                        foreach (var artistName in artists)
                        {
                            if (authorsCSV.Contains(artistName.ToLower()))
                            {
                                return result;
                            }
                        }
                    }
                }

                // Pass 2
                foreach (var result in advancedResults)
                {
                    if (ApiHelper.PrunePunctuation(songObj.Title.ToLower()).Equals(ApiHelper.PrunePunctuation(result.Title.ToLower())))
                    {
                        return result;
                    }
                }

                // Pass 3
                return advancedResults.First();
            }

            advancedResults.Clear();
            await AdvancedSearch(advancedResults, songObj.Artists, songObj.Title, "", token, 5);
            if (advancedResults.Count > 0)
            {
                return advancedResults[0];
            }

            // Fall back to searching for videos
            return await GetTrack((await GetSearchResult(songObj)).Id);
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
        public static async Task<string> GetMaxResThumbnail(SongSearchObject song)
        {
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

        public static async Task<SongSearchObject> GetTrack(Song ytmSong)
        {
            var artistCSV = new StringBuilder();
            foreach (var artist in ytmSong.Artists)
            {
                artistCSV.Append(artist.Name);
                if (artist != ytmSong.Artists.Last())
                {
                    artistCSV.Append(", ");
                }
            }

            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views

            return new SongSearchObject()
            {
                AlbumName = ytmSong.Album.Name,
                Artists = artistCSV.ToString(),
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

        public static async Task<SongSearchObject> GetTrack(string id)
        {
            var video = await youtube.Videos.GetAsync(id);
            return await ConvertSongSearchObject(video);
        }

        public static async Task<SongSearchObject> GetTrack(SongVideoInfo ytmSong, YoutubeExplode.Videos.Video video)
        {
            // Get artist csv
            var artistCSV = new StringBuilder();
            foreach (var artist in ytmSong.Artists)
            {
                artistCSV.Append(artist.Name);
                if (artist != ytmSong.Artists.Last())
                {
                    artistCSV.Append(", ");
                }
            }

            // These values vary between song or video
            string albumName;
            string imageLocation;

            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
            {
                var lines = video.Description.Split("\n");
                albumName = lines[4].Trim(); // Fourth line of ytm description contains album name
                imageLocation = ytmSong.Thumbnails.Last().Url; // Use second youtube music image (larger, 120px)
            }
            else // Normal video
            {
                albumName = video.Title; // Use video title as album name

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
                Artists = artistCSV.ToString(),
                Duration = ytmSong.Duration.TotalSeconds.ToString(),
                Source = "youtube",
                Explicit = !ytmSong.IsFamiliyFriendly,
                Id = ytmSong.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = FormatLargeValue(video.Engagement.ViewCount),
                TrackPosition = "1",
                ReleaseDate = ApiHelper.FormatDateTimeOffset(video.UploadDate),
                Title = ytmSong.Name,
            };
        }

        public static async Task<SongSearchObject> ConvertSongSearchObject(YoutubeExplode.Videos.Video video)
        {
            var song = await ytm.GetSongVideoInfoAsync(video.Id);
            return await GetTrack(song, video);
        }

        public static async Task<SongSearchObject> ConvertSongSearchObject(SongVideoInfo ytmSong)
        {
            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views
            return await GetTrack(ytmSong, video);
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

        public static async Task DownloadAudio(string url, string downloadFolder, string filename, Action<double> progressHandler)
        {
            // if download folder ends with a backslash, remove it
            if (downloadFolder.EndsWith("\\"))
            {
                downloadFolder = downloadFolder.Substring(0, downloadFolder.Length - 1);
            }

            Debug.WriteLine(url + " | " + downloadFolder);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url);
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

                Debug.WriteLine(streamObj.Container + " | " + streamObj.Bitrate + " | " + streamObj.AudioCodec);
            }

            var filePath = $"{downloadFolder}\\{filename}.{extension}";

            var stream = await youtube.Videos.Streams.GetAsync(streamInfo);
            await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, new Progress<double>(progressHandler));
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