using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace FluentDL.Services
{
    internal class YoutubeApi
    {
        private static YoutubeClient youtube = new YoutubeClient();

        public YoutubeApi()
        {
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