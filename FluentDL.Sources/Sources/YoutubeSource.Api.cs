using FluentDL.Core.Helpers;
using FluentDL.Core.Sources;
using FluentDL.Core.Matching;
using FluentDL.Helpers;
using FluentDL.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using VideoLibrary;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models.Info;
using YouTubeMusicAPI.Models.Search;

namespace FluentDL.Services.Sources
{
    internal sealed partial class YoutubeSource
    {
        private readonly YoutubeClient youtube;
        private readonly YouTubeMusicClient ytm;
        private YouTube youtubeAlt = YouTube.Default;

        // YouTube answers some song detail requests with a bot check and no details, at random (about half of them on
        // 2026-10-10, with YouTubeMusicAPI 3.0.11), and the library then throws ArgumentNullException("videoDetails.title").
        // The same request usually works on another try (bug 16).
        private Task<SongVideoInfo> GetSongVideoInfoAsync(string id, CancellationToken token = default) =>
            RetryBotCheckAsync(() => ytm.GetSongVideoInfoAsync(id, token));

        internal static async Task<T> RetryBotCheckAsync<T>(Func<Task<T>> call, int attempts = 3)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await call();
                }
                catch (ArgumentNullException e) when (e.ParamName == "videoDetails.title" && attempt < attempts)
                {
                }
            }
        }

        public async Task GeneralSearch(ICollection<SongSearchObject> itemSource, string query, CancellationToken token, int offset = 0, int limit = 25, bool albumMode = false)
        {
            token.ThrowIfCancellationRequested();
            query = query.Trim(); // Trim the query
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            if (albumMode)
            {
                try
                {
                    var searchResults = ytm.SearchAsync(query, SearchCategory.Albums);
                    var bufferedResults = await searchResults.FetchItemsAsync(offset, limit, token);
                    foreach (var album in bufferedResults)
                    {
                        if (token.IsCancellationRequested)
                            break;
                        var browseId = await ytm.GetAlbumBrowseIdAsync(album.Id); // Get the browse id
                        itemSource.Add(ConvertAlbumSearchObject(await ytm.GetAlbumInfoAsync(browseId)));
                    }
                } catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to search for albums: " + e.ToString());
                }
            }
            else
            {
                try
                {
                    var searchResults = ytm.SearchAsync(query, SearchCategory.Songs);
                    var bufferedResults = await searchResults.FetchItemsAsync(offset, limit, token);
                    foreach (var song in bufferedResults)
                    {
                        if (token.IsCancellationRequested)
                            break;
                        // As in Melora's Music query search, use the returned song rather than refetching its ID.
                        if (song is SongSearchResult track)
                        {
                            itemSource.Add(ConvertSongSearchResult(track));
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e1)
                {
                    Debug.WriteLine("Error searching YTM: " + e1.Message);
                    Serilog.Log.Warning("YouTube Music search failed; error type {ErrorType}; trying video search", e1.GetType().Name);
                    int ct = 0;
                    // Fall back to regular youtube
                    if (offset > 0) {  // Does not support pagination so disable incremental loading 
                        return;
                    }
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
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e2)
                    {
                        Debug.WriteLine("Error searching YouTube: " + e2.Message);
                        Serilog.Log.Warning("YouTube fallback search failed; error type {ErrorType}", e2.GetType().Name);
                    }
                    return;
                }
            }
        }

        public async Task AdvancedSearch(ICollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token = default, int offset = 0, int limit = 25, bool clearResults = true)
        {
            // Youtube doesn't have an advanced search, must be done manually
            if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(albumName))
            {
                return;
            }

            artistName = TextNormalization.RemoveAccentsAndSymbols(artistName.Trim());
            trackName = TextNormalization.RemoveAccentsAndSymbols(trackName.Trim());
            albumName = TextNormalization.RemoveAccentsAndSymbols(albumName.Trim());

            if (clearResults)
            {
                itemSource.Clear();
            }

            bool isArtistSpecified = !string.IsNullOrWhiteSpace(artistName);
            bool isTrackSpecified = !string.IsNullOrWhiteSpace(trackName);

            var trackIdList = new HashSet<string>();
            var remainingOffset = offset;

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
                            if (FuzzyMatch.CloseMatch(queryArtist.Name, artistName))
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

                    if (oneArtistMatch && FuzzyMatch.CloseMatch(album.Name, albumName)) // This album result substring matches
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

                                var song = await GetSongVideoInfoAsync(playlistVideo.Id);

                                bool valid = true; // True as default

                                if (isArtistSpecified) // Album and artist specified
                                {
                                    if (isTrackSpecified) // Album, artist, track specified
                                    {
                                        oneArtistMatch = false;
                                        foreach (var artist in song.Artists) // Check if at least one artist matches
                                        {
                                            if (FuzzyMatch.CloseMatch(artist.Name, artistName))
                                            {
                                                oneArtistMatch = true;
                                                break;
                                            }
                                        }

                                        valid = oneArtistMatch && FuzzyMatch.CloseMatch(trackName, song.Name);
                                    }
                                }
                                else if (isTrackSpecified) // Album and track specified
                                {
                                    if (isTrackSpecified) // Album and track specified
                                    {
                                        valid = FuzzyMatch.CloseMatch(trackName, song.Name);
                                    }
                                }

                                if (valid && !trackIdList.Contains(song.Id)) // If valid and not already added
                                {
                                    if (remainingOffset > 0)
                                    {
                                        remainingOffset--;
                                        continue;
                                    }

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
                                var song = await GetSongVideoInfoAsync(albumSongInfo.Id); // Get full track info

                                bool valid = true; // True as default

                                if (isArtistSpecified) // Album and artist specified
                                {
                                    if (isTrackSpecified) // Album, artist, track specified
                                    {
                                        oneArtistMatch = false;
                                        foreach (var artist in song.Artists) // Check if at least one artist matches
                                        {
                                            if (FuzzyMatch.CloseMatch(artist.Name, artistName))
                                            {
                                                oneArtistMatch = true;
                                                break;
                                            }
                                        }

                                        valid = oneArtistMatch && FuzzyMatch.CloseMatch(trackName, song.Name);
                                    }
                                }
                                else if (isTrackSpecified) // Album and track specified
                                {
                                    if (isTrackSpecified) // Album and track specified
                                    {
                                        valid = FuzzyMatch.CloseMatch(trackName, song.Name);
                                    }
                                }

                                if (valid && !trackIdList.Contains(song.Id)) // If valid and not already added
                                {
                                    if (remainingOffset > 0)
                                    {
                                        remainingOffset--;
                                        continue;
                                    }

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
                        var song = await GetSongVideoInfoAsync(result.Id);

                        bool oneArtistMatch = false;
                        foreach (var artist in song.Artists) // Check if at least one artist matches
                        {
                            if (FuzzyMatch.CloseMatch(artist.Name, artistName))
                            {
                                oneArtistMatch = true;
                                break;
                            }
                        }

                        if (oneArtistMatch && FuzzyMatch.CloseMatch(song.Name, trackName)) // If at least one artist match and track name close match
                        {
                            if (!trackIdList.Contains(song.Id)) // If not already added
                            {
                                if (remainingOffset > 0)
                                {
                                    remainingOffset--;
                                    continue;
                                }

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
                                    if (FuzzyMatch.CloseMatch(artist.Name, artistName))
                                    {
                                        oneArtistMatch = true;
                                        break;
                                    }
                                }

                                if (oneArtistMatch && !trackIdList.Contains(song.Id)) // If not already added
                                {
                                    if (remainingOffset > 0)
                                    {
                                        remainingOffset--;
                                        continue;
                                    }

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

        // A failed lookup (YouTube bot-checking this machine, a removed video) is reported as an error status instead of
        // thrown; the exception used to crash the app (bug 20).
        public async Task AddTracksFromLink(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
        {
            try
            {
                await AddTracksFromLinkUnguarded(itemSource, url, token, statusUpdate, albumMode);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Debug.WriteLine(e);
                statusUpdate?.Invoke(StatusLevel.Error, StatusMarkup.Format($"<b>YouTube</b>   Error loading <a href='{url}'>{url}</a>: {e.Message}"));
            }
        }

        private async Task AddTracksFromLinkUnguarded(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
        {
            if (url.StartsWith("https://www.youtube.com/watch?") || url.StartsWith("https://youtube.com/watch?")) // Youtube video
            {
                var video = await youtube.Videos.GetAsync(url);
                var songObj = await ConvertSongSearchObject(video);
                if (songObj == null)  // Some error occurred
                {
                    statusUpdate?.Invoke(StatusLevel.Error, StatusMarkup.Format($"<b>YouTube</b>   Error loading track from <a href='{url}'>{url}</a>"));
                    return;
                }

                itemSource.Add(songObj);

                if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
                {
                    statusUpdate?.Invoke(StatusLevel.Success, StatusMarkup.Format($"<b>YouTube</b>   Loaded track <a href='{url}'>{songObj.Title}</a>"));
                }
                else // Video
                {
                    statusUpdate?.Invoke(StatusLevel.Success, StatusMarkup.Format($"<b>YouTube</b>   Loaded video <a href='{url}'>{songObj.Title}</a>"));
                }
            }

            if (url.StartsWith("https://music.youtube.com/watch?v=")) // Youtube music has both songs and videos
            {
                var video = await youtube.Videos.GetAsync(url);
                var songObj = await ConvertSongSearchObject(video);
                if (songObj == null)  // Some error occurred
                {
                    statusUpdate?.Invoke(StatusLevel.Error, StatusMarkup.Format($"<b>YouTube Music</b>   Error loading track from <a href='{url}'>{url}</a>"));
                    return;
                }

                itemSource.Add(songObj);

                if (video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
                {
                    statusUpdate?.Invoke(StatusLevel.Success, StatusMarkup.Format($"<b>YouTube Music</b>   Loaded track <a href='{url}'>{songObj.Title}</a>"));
                }
                else // Video
                {
                    statusUpdate?.Invoke(StatusLevel.Success, StatusMarkup.Format($"<b>YouTube Music</b>   Loaded video <a href='{url}'>{songObj.Title}</a>"));
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
                statusUpdate?.Invoke(StatusLevel.Informational, StatusMarkup.Format($"<b>YouTube</b>   Loading playlist <a href='{url}'>{playlistName}</a>"), -1);

                var failCount = 0;
                try
                {
                    await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(url, token))
                    {
                        if (token.IsCancellationRequested)
                        {
                            statusUpdate?.Invoke(StatusLevel.Warning, StatusMarkup.Format($"<b>YouTube</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>"));
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
                        statusUpdate?.Invoke(StatusLevel.Warning, StatusMarkup.Format($"<b>YouTube</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>"));
                        return;
                    }

                    Debug.WriteLine("Error loading playlist: " + e.Message);
                    statusUpdate?.Invoke(StatusLevel.Error, StatusMarkup.Format($"<b>YouTube</b>   Error loading playlist <a href='{url}'>{playlistName}</a>"));
                    return;
                }

                // Replace the loading message with a result message
                if (failCount > 0) // If some failed
                {
                    statusUpdate?.Invoke(StatusLevel.Warning, StatusMarkup.Format($"<b>YouTube</b>   Loaded playlist <a href='{url}'>{playlistName}</a> with {failCount} failed {(failCount == 1 ? "track" : "tracks")}"));
                }
                else // All loaded successfully
                {
                    statusUpdate?.Invoke(StatusLevel.Success, StatusMarkup.Format($"<b>YouTube</b>   Loaded playlist <a href='{url}'>{playlistName}</a>"));
                }
            }
        }


        public async Task<YoutubeExplode.Search.VideoSearchResult?> GetSearchResult(SongSearchObject song)
        {
            // Convert artists csv to array
            var artists = song.Artists.Split(", ");
            for (int i = 0; i < artists.Length; i++)
            {
                artists[i] = artists[i].ToLower();
            }

            // Durations can be seconds or clock text such as "6:10"; double.Parse threw on the latter (bug 6).
            if (!DurationParser.TryParse(song.Duration, out var songDuration))
            {
                return null;
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

                if (Math.Abs(totalSeconds - songDuration.TotalSeconds) <= 1)
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
                var pruneTitle = TitleNormalizer.Compact(result.Title);
                var songTitle = TitleNormalizer.Compact(song.Title);

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
                var pruneTitle = TitleNormalizer.Compact(result.Title);
                var songTitle = TitleNormalizer.Compact(song.Title);

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
                var pruneTitle = TitleNormalizer.Compact(result.Title);
                var songTitle = TitleNormalizer.Compact(song.Title);

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
                var pruneTitle = TitleNormalizer.Compact(result.Title);
                var songTitle = TitleNormalizer.Compact(song.Title);
                if (pruneTitle.Contains(songTitle) && result.Title.ToLower().Contains("audio"))
                {
                    return result;
                }
            }


            // Pass 6
            foreach (var result in results)
            {
                var pruneTitle = TitleNormalizer.Compact(result.Title);
                var songTitle = TitleNormalizer.Compact(song.Title);
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

        public async Task<SongSearchObject?> GetYoutubeTrack(SongSearchObject songObj, CancellationToken token = default, ConversionUpdateCallback? callback = null)
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
            var albumQuery = $"{artistName} {albumName}";

            // A live or re-recorded version can contain the title ("夜に駆ける (From "THE FIRST TAKE")"), so when the
            // song's length is known, a title match within 3 s of it wins. A title match of another length is kept and
            // used only if the album and song searches find none of the same length: releases of the same recording can
            // differ by several seconds (the Yo-Yo Ma Prelude is 146 s on Deezer, 152 s on YouTube).
            var hasLength = DurationParser.TryParse(songObj.Duration, out var songLength) && songLength > TimeSpan.Zero;
            bool SameLength(TimeSpan candidate) => !hasLength || (candidate - songLength).Duration() <= TimeSpan.FromSeconds(3);
            SongVideoInfo? otherLength = null;

            int ct = 0;
            try
            {
                // Start searching through albums
                await foreach (var result in ytm.SearchAsync(albumQuery, SearchCategory.Albums))
                {
                    if (token.IsCancellationRequested) return null; // Check if task is cancelled
                    if (ct++ >= 5) break;

                    var browseId = await ytm.GetAlbumBrowseIdAsync(result.Id); // Get the browse id
                    var album = await ytm.GetAlbumInfoAsync(browseId);

                    // Ensure album artist matches
                    bool oneArtistMatch = album.Artists.Any(queryArtist => artists.Any(artist => FuzzyMatch.CloseMatch(queryArtist.Name, artist)));

                        if (oneArtistMatch && FuzzyMatch.CloseMatch(album.Name, albumName)) // This album result substring matches
                    {
                        var playlistUrl = $"https://music.youtube.com/playlist?list={album.Id}";

                        try
                        {
                            await foreach (var playlistVideo in youtube.Playlists.GetVideosAsync(playlistUrl, token))
                            {
                                var song = await GetSongVideoInfoAsync(playlistVideo.Id, token);
                                if (token.IsCancellationRequested)
                                {
                                    return null;
                                }

                                oneArtistMatch = song.Artists.Any(queryArtist => artists.Any(artist => FuzzyMatch.CloseMatch(queryArtist.Name, artist)));

                                if (oneArtistMatch && FuzzyMatch.CloseMatch(song.Name, trackName) && !SameLength(song.Duration))
                                {
                                    otherLength ??= song;
                                }
                                else if (oneArtistMatch && FuzzyMatch.CloseMatch(song.Name, trackName))
                                {
                                    var retObj = await ConvertSongSearchObject(song);
                                    if (retObj != null)
                                    {
                                        callback?.Invoke(StatusLevel.Warning, retObj); // Not found by ISRC
                                        return retObj;
                                    }
                                }
                            }
                        }
                        catch (Exception e) // Can crash on invalid playlist or token cancel (for some reason)
                        {
                            Debug.WriteLine("Error loading playlist: " + e.Message);
                            if (token.IsCancellationRequested) return null;
                            // Otherwise try the next album, then the song and video searches below (bug 5: a failed
                            // album used to end the whole match).
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error searching for album: " + e.ToString());
            }

            // Try searching without album
            var query = $"{artistName} {trackName}";
            ct = 0;
            try
            {
                await foreach (var res in ytm.SearchAsync(query, SearchCategory.Songs))
                {
                    if (ct++ >= 20) break; // ct was never incremented, so this never stopped the search (bug 4)
                    if (token.IsCancellationRequested) return null; // Check if task is cancelled
                    if (TextNormalization.PrunePunctuation(songObj.Title.ToLower()).Equals(TextNormalization.PrunePunctuation(res.Name.ToLower())))
                    {
                        var fullRes = await GetSongVideoInfoAsync(res.Id);
                        foreach (var artistResult in fullRes.Artists)
                        {
                            if (artists.Any(artist => FuzzyMatch.CloseMatch(artistResult.Name, artist)) && !SameLength(fullRes.Duration))
                            {
                                otherLength ??= fullRes;
                                break;
                            }
                            if (artists.Any(artist => FuzzyMatch.CloseMatch(artistResult.Name, artist)))
                            {
                                var retObj = await ConvertSongSearchObject(fullRes);
                                callback?.Invoke(StatusLevel.Warning, retObj); // Not found by ISRC
                                return retObj;
                            }
                        }
                    }
                }
            } catch (Exception e)
            {
                Debug.WriteLine("Error searching without album: " + e.ToString());
            }

            // A title match of another length, which these searches used to return before checking length
            if (otherLength != null && await ConvertSongSearchObject(otherLength) is { } otherLengthObj)
            {
                callback?.Invoke(StatusLevel.Warning, otherLengthObj); // Not found by ISRC
                return otherLengthObj;
            }

            // Fall back to searching for videos
            var searchResult = await GetSearchResult(songObj);
            if (searchResult == null)
            {
                callback?.Invoke(StatusLevel.Error, songObj);  // Show error badge with original object
                return null;
            }

            var ret = await GetTrack(searchResult.Id);
            if (ret == null) // If not found
            {
                callback?.Invoke(StatusLevel.Error, songObj);  // Show error badge with original object
                return null;
            }
            callback?.Invoke(StatusLevel.Warning, ret); // Not found by ISRC
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
        public async Task<string?> GetMaxResThumbnail(SongSearchObject song)
        {
            if (song.ImageLocation == null)
                return null;

            var pattern = @"=w\d+-h\d+[^&]*";
            if (Regex.IsMatch(song.ImageLocation, pattern))
            {
                // Replace the matched size/modifier segment with the desired max-res suffix
                return Regex.Replace(song.ImageLocation, pattern, "=w544-h544-l90-rj");
            }

            try
            {
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
            } catch
            {
                return song.ImageLocation;
            }
        }

        public string GetMaxResThumbnail(SongVideoInfo? ytmSong, YoutubeExplode.Videos.Video video)
        {
            if (ytmSong !=null && ytmSong.Thumbnails.Length > 0 && 
                video.Description.StartsWith("Provided to YouTube by") && video.Description.Contains("\u2117")) // Song
            {
                var urlMaxWidth = ytmSong.Thumbnails.MaxBy(x => x.Width)?.Url;
                if (urlMaxWidth != null)
                {
                    return urlMaxWidth;
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

        // Search has no video upload date or view count. Leave them unknown instead of making per-row requests.
        internal static SongSearchObject ConvertSongSearchResult(SongSearchResult song) => new()
        {
            Source = SourceIds.YouTube,
            Id = song.Id,
            Title = song.Name,
            Artists = string.Join(", ", song.Artists.Select(a => a.Name)),
            AlbumName = song.Album?.Name ?? "",
            Duration = song.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture),
            Explicit = song.IsExplicit,
            ImageLocation = song.Thumbnails.MaxBy(t => t.Width)?.Url ?? "",
            ReleaseDate = "",
            Rank = "",
        };

        public async Task<SongSearchObject> GetTrack(SongSearchResult ytmSong)
        {
            var artistString = string.Join(", ", ytmSong.Artists.Where(a => a.Id != null).Select(a => a.Name).ToList());

            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views

            return new SongSearchObject()
            {
                AlbumName = ytmSong.Album.Name,
                Artists = artistString,
                Duration = ytmSong.Duration.TotalSeconds.ToString(),
                Source = SourceIds.YouTube,
                Explicit = ytmSong.IsExplicit,
                Id = ytmSong.Id,
                ImageLocation = ytmSong.Thumbnails.Last().Url,
                Isrc = null,
                Rank = FormatLargeValue(video.Engagement.ViewCount),
                TrackPosition = "1",
                ReleaseDate = DisplayFormat.FormatDateTimeOffset(video.UploadDate),
                Title = ytmSong.Name,
            };
        }

        public async Task<SongSearchObject?> GetTrack(string id)
        {
            var video = await youtube.Videos.GetAsync(id);
            return await ConvertSongSearchObject(video);
        }

        public SongSearchObject GetTrack(SongVideoInfo? ytmSong, YoutubeExplode.Videos.Video video)
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
                Source = SourceIds.YouTube,
                Explicit = !ytmSong?.IsFamiliyFriendly ?? false,
                Id = ytmSong?.Id ?? video.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                Rank = FormatLargeValue(video.Engagement.ViewCount),
                TrackPosition = "1",
                ReleaseDate = DisplayFormat.FormatDateTimeOffset(video.UploadDate),
                Title = ytmSong?.Name ?? video.Title,
            };
        }

        public async Task<SongSearchObject> ConvertSongSearchObject(YoutubeExplode.Videos.Video video)
        {
            SongVideoInfo? ytmInfo = null;
            try
            {
                ytmInfo = await GetSongVideoInfoAsync(video.Id);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error converting song search object: " + e.Message);
            }
            return GetTrack(ytmInfo, video);
        }

        public async Task<SongSearchObject> ConvertSongSearchObject(SongVideoInfo ytmSong)
        {
            var video = await youtube.Videos.GetAsync(ytmSong.Id); // Get youtube explode video object, has more technical info like release date + views
            return GetTrack(ytmSong, video);
        }

        public AlbumSearchObject ConvertAlbumSearchObject(AlbumInfo ytmAlbum) {
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
                Source = SourceIds.YouTube,
                Explicit = false,
                Id = ytmAlbum.Id,
                ImageLocation = imageLocation,
                Isrc = null,
                Rank = "",
                TrackPosition = "1",
                ReleaseDate = $"{ytmAlbum.ReleaseYear}-01-01",
                Title = ytmAlbum.Name,
                TracksCount = ytmAlbum.SongCount,
                TrackList = ytmAlbum.Songs.Select(song => ConvertSongSearchObject(song, ytmAlbum)).ToList()
            };
        }

        public SongSearchObject ConvertSongSearchObject(AlbumSong song, AlbumInfo album)
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
                Source = SourceIds.YouTube,
                Explicit = song.IsExplicit,
                Id = song.Id,
                ImageLocation = imageLocation,
                Isrc = null,
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
        public string FormatLargeValue(long value) => FluentDL.Core.Helpers.DisplayFormat.FormatLargeValue(value);


        public async Task DownloadAudio(string filePath, VideoId id, IProgress<ProgressData> progress)
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

            var uniqueId = DateTime.Now.Ticks;
            var totalBytes = streamInfo.Size.Bytes;
            long lastBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastTime = stopwatch.Elapsed; 
            object progressLock = new();

            var progressInner = new Progress<double>((percent) =>
            {
                var currentBytes = (long)Math.Round(percent * totalBytes);
                double bps = 0;
                lock (progressLock)
                {
                    var elapsedTime = stopwatch.Elapsed - lastTime;
                    var deltaBytes = currentBytes - lastBytes; 
                    bps = deltaBytes / elapsedTime.TotalSeconds; 
                    // Next iteration
                    lastBytes = currentBytes;
                    lastTime = stopwatch.Elapsed; 
                }
                progress.Report(new ProgressData()
                {
                    uniqueId = uniqueId,
                    totalBytes = totalBytes,
                    currentBytes = currentBytes,
                    bps = bps,
                });
            });

            try
            {
                await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progressInner);
            }
            catch
            {
                throw;
            }
            finally
            {
                progress.Report(new ProgressData()
                {
                    uniqueId = uniqueId,
                    totalBytes = totalBytes,
                    currentBytes = totalBytes,
                    bps = 0
                });
            }
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
        public async Task DownloadAudioAAC(string filePath, VideoId id)
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

        public async Task UpdateMetadata(string filePath, string id)
        {
            var video = await youtube.Videos.GetAsync(id);
            var ytmSong = await GetSongVideoInfoAsync(id);

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
                    Artists = artists?.ToArray(),
                    AlbumArtPath = GetMaxResThumbnail(ytmSong, video),
                    AlbumName = albumName,
                    AlbumArtists = [artists?.FirstOrDefault("") ?? ""],
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

        // Get stream with minimum bitrate
        public async Task<string> AudioStreamWorstUrl(string url)
        {
            try
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
            catch (Exception e)
            {
                Debug.WriteLine("Error getting worst audio stream: " + e.Message);
                return "";
            }
        }

    }
}
