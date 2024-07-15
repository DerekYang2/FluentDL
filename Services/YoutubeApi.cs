using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Search;

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

                ct++;
                if (ct >= 15 || results.Count >= 5)
                {
                    break;
                }
            }

            // 1: if author contains artist name
            // 2: if title contains song title and artist name and has "audio" in title
            // 3: if title contains song title and has "audio" in title
            // 4: if title contains song title and artist name
            // 5: if title contains song title
            // 6: take the first result


            // Pass 1
            foreach (var result in results)
            {
                bool containsArtist = false;

                foreach (var artist in artists)
                {
                    if (result.Author.ChannelTitle.ToLower().Contains(artist))
                    {
                        containsArtist = true;
                        break;
                    }
                }

                if (containsArtist)
                {
                    return result;
                }
            }

            // Pass 2
            foreach (var result in results)
            {
                if (result.Title.ToLower().Contains(song.Title.ToLower()) && result.Title.ToLower().Contains(artists[0]) && result.Title.ToLower().Contains("audio"))
                {
                    return result;
                }
            }

            // Pass 3
            foreach (var result in results)
            {
                if (result.Title.ToLower().Contains(song.Title.ToLower()) && result.Title.ToLower().Contains("audio"))
                {
                    return result;
                }
            }

            // Pass 4
            foreach (var result in results)
            {
                bool containsArtist = false;

                foreach (var artist in artists)
                {
                    if (result.Author.ChannelTitle.ToLower().Contains(artist))
                    {
                        containsArtist = true;
                        break;
                    }
                }

                if (result.Title.ToLower().Contains(song.Title.ToLower()) && containsArtist)
                {
                    return result;
                }
            }

            // Pass 5
            foreach (var result in results)
            {
                if (result.Title.ToLower().Contains(song.Title.ToLower()))
                {
                    return result;
                }
            }

            // Pass 6
            if (results.Count > 0)
            {
                return results[0];
            }
            else
            {
                return null;
            }
        }
    }
}