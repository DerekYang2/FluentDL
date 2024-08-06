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

        public static async Task<SongSearchObject?> GetYoutubeTrack(SongSearchObject songObj)
        {
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
        public static async Task<Uri> GetMaxResThumbnail(SongSearchObject song)
        {
            if (song.ImageLocation.StartsWith("https://lh3.googleusercontent.com")) // If youtube music image
            {
                string imageUrl = song.ImageLocation;
                // Delete everything after the last =
                var i = imageUrl.LastIndexOf("=");
                imageUrl = imageUrl.Substring(0, i + 1); // Include everything up to and including the =
                imageUrl += "w544-h544-l90-rj"; // Add the max res version
                return new Uri(imageUrl);
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

            return new Uri(video.Thumbnails[idx].Url);
        }

        public static async Task<SongSearchObject> GetTrack(SongVideoInfo ytmSong, YoutubeExplode.Videos.Video video)
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

            var albumName = ytmSong.Name;

            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Youtube music
            {
                var lines = video.Description.Split("\n");
                albumName = lines[4].Trim(); // Fourth line of ytm description contains album name
            }
            else // Should not happen
            {
                throw new Exception("Youtube music object description failed to parse: " + video.Description);
            }

            return new SongSearchObject()
            {
                AlbumName = albumName,
                Artists = artistCSV.ToString(),
                Duration = ytmSong.Duration.TotalSeconds.ToString(),
                Source = "youtube",
                Explicit = !ytmSong.IsFamiliyFriendly,
                Id = ytmSong.Id,
                ImageLocation = ytmSong.Thumbnails.Last().Url,
                Isrc = null,
                LocalBitmapImage = null,
                Rank = ytmSong.ViewsCount.ToString(),
                TrackPosition = "1",
                ReleaseDate = ApiHelper.FormatDateTimeOffset(ytmSong.UploadedAt),
                Title = ytmSong.Name,
            };
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
                Rank = video.Engagement.ViewCount.ToString(),
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

        public static async Task<SongSearchObject> ConvertSongSearchObject(YoutubeExplode.Videos.Video video)
        {
            var song = await ytm.GetSongVideoInfoAsync(video.Id);

            if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Special case: if YouTube music song
            {
                return await GetTrack(song, video);
            }

            // Normal case: youtube video

            // Get artist csv through ytm object
            var artistCSV = new StringBuilder();
            foreach (var artist in song.Artists)
            {
                artistCSV.Append(artist.Name);
                if (artist != song.Artists.Last())
                {
                    artistCSV.Append(", ");
                }
            }

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

            return new SongSearchObject()
            {
                AlbumName = video.Title, // Just use the video title as album name
                Artists = artistCSV.ToString(),
                Duration = ((int)video.Duration.Value.TotalSeconds).ToString(),
                Explicit = !song.IsFamiliyFriendly,
                Source = "youtube",
                Id = video.Id,
                TrackPosition = "1",
                ImageLocation = video.Thumbnails[idx].Url,
                LocalBitmapImage = null,
                Rank = video.Engagement.ViewCount.ToString(),
                ReleaseDate = ApiHelper.FormatDateTimeOffset(video.UploadDate),
                Title = song.Name,
                Isrc = null
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

        public static async Task DownloadAudio(string url, string downloadFolder, string filename)
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
            extension = "aac";
            /*
             // BELOW IS CODE FOR OPUS CODEC
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
            */

            var filePath = $"{downloadFolder}\\{filename}.{extension}";

            var stream = await youtube.Videos.Streams.GetAsync(streamInfo);
            await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath);
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