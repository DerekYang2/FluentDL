using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Context;
using BitmapImage = Microsoft.UI.Xaml.Media.Imaging.BitmapImage;
using FluentDL.Services.Sources;

namespace FluentDL.Services;

/// <summary>
/// Downloads tracks and albums: naming, the per-source download, tags, lyrics, ReplayGain and the album cover. Moved
/// unchanged from ApiHelper; Phase 6 replaces its calls into the source classes with role interfaces.
/// </summary>
public sealed class DownloadService(SourceRegistry sources)
{
    private static readonly HttpClient client = new HttpClient();

    private static string GetTrackSubfolderPath(SongSearchObject song, string baseDirectory, string? trackWildcard)
    {
        var wildCardStr = trackWildcard;
        if (string.IsNullOrWhiteSpace(wildCardStr))
        {
            wildCardStr = "{artist}/{year} - {album}";
        }

        var subfolderName = ApiHelper.GetSafeDirectoryPath(ApiHelper.EvaluateWildcard(song, wildCardStr));
        return Path.Combine(baseDirectory, subfolderName);
    }

    private static List<string> GetAlbumCoverDirectories(SongSearchObject album, IEnumerable<string> downloadPaths, string baseDirectory, bool createTrackSubfolder)
    {
        if (!createTrackSubfolder)
        {
            return [];
        }

        var albumMarker = ApiHelper.GetSafeDirectoryPath(ApiHelper.EvaluateWildcard(album, "{album}"));
        if (string.IsNullOrWhiteSpace(albumMarker))
        {
            return [];
        }

        var coverDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trackSubfolders = downloadPaths
            .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var trackSubfolder in trackSubfolders)
        {
            if (!trackSubfolder.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(baseDirectory, trackSubfolder);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();

            var albumSegmentIndex = Array.FindIndex(segments, segment =>
                segment.Contains(albumMarker, StringComparison.OrdinalIgnoreCase));
            if (albumSegmentIndex < 0)
            {
                continue;
            }

            var coverPathSegments = segments.Take(albumSegmentIndex + 1).ToArray();
            if (coverPathSegments.Length == 0)
            {
                continue;
            }

            var coverDirectory = Path.Combine([baseDirectory, .. coverPathSegments]);
            coverDirectories.Add(coverDirectory);
        }

        return coverDirectories.ToList();
    }

    private static async Task DownloadLyricsAsync(SongSearchObject song, string directory, string fileName, TaskCompletionSource<bool>? downloadTcs = default)
    {
        try
        {
            // Check if lyrics download is enabled
            var downloadLyricsEnabled = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.DownloadLyrics);
            if (!downloadLyricsEnabled)
                return;

            // Get lyrics settings
            var lyricsExtension = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.LyricsExtension) ?? 0; // 0 = .lrc, 1 = .txt
            var lyricsDir = await SettingsViewModel.GetSetting<string?>(SettingsViewModel.LyricsDirectory) ?? "";

            // Determine lyrics file extension
            var extension = lyricsExtension == 0 ? ".lrc" : ".txt";

            // Determine lyrics save directory
            string lyricsSaveDir;
            if (string.IsNullOrWhiteSpace(lyricsDir))
            {
                // Same directory as track
                lyricsSaveDir = directory;
            }
            else
            {
                lyricsSaveDir = lyricsDir;
            }

            if (string.IsNullOrWhiteSpace(lyricsSaveDir))
                return;

            // Get lyrics from service
            var lyricService = App.GetService<ILyricService>();
            var lyrics = await lyricService.GetLyricsAsync(song);

            if (lyrics == null)
                return; // No lyrics found

            // Build lyrics file path (same name as track, different extension)
            var lyricsFilePath = Path.Combine(lyricsSaveDir, fileName + extension);

            // Wait for track download status (must be success to save lyrics)
            if (downloadTcs != null)
            {
                var downloadSuccess = await downloadTcs.Task;
                if (!downloadSuccess)
                {
                    return;
                }
            }

            // Write lyrics to file
            await File.WriteAllTextAsync(lyricsFilePath, lyrics);
            Log.Debug("Lyrics saved");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Lyrics download failed");
        }
    }

    private static async Task<string> DownloadTrackInternal(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback = default)
    {
        using var operation = LogContext.PushProperty("OperationId", Guid.NewGuid().ToString("N"));
        var timer = Stopwatch.StartNew();
        Log.Information("Track download started; provider {Provider}", song.Source);
        try
        {
            var path = await DownloadTrackCore(song, directory, progress, callback);
            if (!string.IsNullOrEmpty(path))
            {
                Log.Information("Track download completed in {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);
            }
            return path;
        }
        catch (OperationCanceledException ex)
        {
            Log.Warning(ex, "Track download interrupted by cancellation or timeout after {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Track download failed before completion after {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);
            throw;
        }
    }

    private static async Task<string> DownloadTrackCore(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback)
    {
        // Spotify case (web player songs do not have full metadata)
        if (song.Source == SourceIds.Spotify && (string.IsNullOrWhiteSpace(song.ReleaseDate) || string.IsNullOrWhiteSpace(song.TrackPosition)))
        {
            song = await SpotifyApi.GetTrack(song.Id) ?? song;
        }

        // Create file name
        var wildCardStr = await SettingsViewModel.GetSetting<string>(SettingsViewModel.FileWildcard);
        if (string.IsNullOrWhiteSpace(wildCardStr))
        {
            wildCardStr = string.IsNullOrWhiteSpace(song.Isrc) ? "{position}. {artist} - {title}" : "{position}. {artist} - {title} [{isrc}]";
        }
        var fileName = ApiHelper.GetSafeFilename(ApiHelper.EvaluateWildcard(song, wildCardStr));

        var locationNoExt = Path.Combine(directory, fileName);

        // Run lyrics download in parallel
        var downloadTcs = new TaskCompletionSource<bool>();
        _ = Task.Run(async () => await DownloadLyricsAsync(song, directory, fileName, downloadTcs));


        if (song.Source == SourceIds.YouTube)
        {
            var flacLocation = Path.Combine(directory, fileName + ".flac");
            var opusLocation = Path.Combine(directory, fileName + ".opus");
            var m4aLocation = Path.Combine(directory, fileName + ".m4a");

            try
            {
                // 0 - opus
                // 1 - flac (opus)
                // 2 - m4a (aac)
                var settingIdx = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.YoutubeQuality) ?? 0;
                var useYtdlp = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.UseYtdlp);
                var ytdlpPath = useYtdlp ? await SettingsViewModel.GetSetting<string?>(SettingsViewModel.YtdlpPath) : null;

                // Ensure the base FluentDL temp directory exists
                var tempBaseDir = Path.Combine(Path.GetTempPath(), "FluentDL");
                Directory.CreateDirectory(tempBaseDir);

                // --- AAC / M4A PATH ---
                if (settingIdx == 2)
                {
                    if (File.Exists(m4aLocation) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false)
                    {
                        throw new Exception("File already exists.");
                    }

                    var tempGuid = Guid.NewGuid().ToString("N");
                    var tempM4a = Path.Combine(tempBaseDir, $"download-{tempGuid}.m4a");
                    var tempMp4 = Path.Combine(tempBaseDir, $"download-{tempGuid}.mp4");

                    try
                    {
                        if (useYtdlp)
                        {
                            // yt-dlp extracts bestaudio[acodec=mp4a.40.2] which natively saves as .m4a
                            await YoutubeApi.DownloadAudioAACYTDLP(tempM4a, $"https://www.youtube.com/watch?v={song.Id}", ytdlpPath);
                            // Move the temp file to the final destination
                            File.Move(tempM4a, m4aLocation, overwrite: true);
                        }
                        else
                        {
                            if (!FFmpegRunner.IsInitialized) throw new Exception("FFmpeg is not initialized.");
                            await YoutubeApi.DownloadAudioAAC(tempMp4, song.Id);
                            await FFmpegRunner.ConvertMp4ToM4aAsync(tempMp4, m4aLocation);
                        }

                        await YoutubeApi.UpdateMetadata(m4aLocation, song.Id);
                        downloadTcs.SetResult(true);
                        callback?.Invoke(StatusLevel.Success, song, m4aLocation);
                        return m4aLocation;
                    }
                    finally
                    {
                        // Clean up possible temp files
                        if (File.Exists(tempM4a)) File.Delete(tempM4a);
                        if (File.Exists(tempMp4)) File.Delete(tempMp4);
                    }
                }

                // --- OPUS / FLAC PATH ---
                var finalLocation = settingIdx == 1 ? flacLocation : opusLocation;

                if (File.Exists(finalLocation) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false)
                {
                    throw new Exception("File already exists.");
                }

                // Generate a single unique temporary path for the WebM download
                var tempWebmLocation = Path.Combine(tempBaseDir, $"download-{Guid.NewGuid():N}.webm");

                try
                {
                    // Both YT-DLP and Native download a .webm file
                    if (useYtdlp)
                    {
                        await YoutubeApi.DownloadAudioYTDLP(tempWebmLocation, $"https://www.youtube.com/watch?v={song.Id}", ytdlpPath);
                    }
                    else
                    {
                        await YoutubeApi.DownloadAudio(tempWebmLocation, song.Id, progress);
                    }

                    if (!FFmpegRunner.IsInitialized) throw new Exception("FFmpeg is not initialized.");

                    if (settingIdx == 1) // FLAC
                    {
                        await FFmpegRunner.ConvertToFlacAsync(tempWebmLocation, flacLocation);
                    }
                    else // OPUS
                    {
                        await FFmpegRunner.RemuxOpusAsync(tempWebmLocation, opusLocation);
                    }

                    // Finalize Metadata and Return
                    await YoutubeApi.UpdateMetadata(finalLocation, song.Id);
                    downloadTcs.SetResult(true);
                    callback?.Invoke(StatusLevel.Success, song, finalLocation);
                    return finalLocation;
                }
                finally
                {
                    // Always clean up the temporary .webm file
                    if (File.Exists(tempWebmLocation))
                    {
                        File.Delete(tempWebmLocation);
                    }
                }
            }
            catch (Exception e)
            {
                callback?.Invoke(StatusLevel.Error, song, e.Message);
                Log.Error(e, "YouTube download or post-processing failed");
            }
        }

        if (song.Source == SourceIds.Deezer)
        {
            try
            {
                var resultPath = await DeezerApi.DownloadTrack(locationNoExt, song, use128Fallback: true, progress: progress);
                await DeezerApi.UpdateMetadata(resultPath, song.Id);
                downloadTcs.SetResult(true);
                callback?.Invoke(StatusLevel.Success, song, resultPath); // Assume success
                return resultPath;
            }
            catch (Exception e)
            {
                callback?.Invoke(StatusLevel.Error, song, e.Message);
                Log.Error(e, "Deezer download or post-processing failed");
            }
        }

        if (song.Source == SourceIds.Qobuz)
        {
            try
            {
                var resultPath = await QobuzApi.DownloadTrack(locationNoExt, song, progress);
                await QobuzApi.UpdateMetadata(resultPath, song.Id);
                downloadTcs.SetResult(true);
                callback?.Invoke(StatusLevel.Success, song, resultPath); // Assume success
                return resultPath;
            }
            catch (Exception e)
            {
                callback?.Invoke(StatusLevel.Error, song, e.Message);
                Log.Error(e, "Qobuz download or post-processing failed");
            }
        }

        if (song.Source == SourceIds.Spotify)
        {
            try
            {
                var resultPath = await SpotifyApi.DownloadEquivalentTrack(locationNoExt, song, progress, false, callback);

                if (resultPath != null)
                {
                    await SpotifyApi.UpdateMetadata(resultPath, song.Id);
                    downloadTcs.SetResult(true);
                    return resultPath;
                }

                callback?.Invoke(StatusLevel.Error, song); // Null - error
                Log.Error("Spotify equivalent-track download returned no result");
            } catch (Exception e) 
            {
                Log.Error(e, "Spotify equivalent-track download or post-processing failed");
                callback?.Invoke(StatusLevel.Error, song, e.Message);
            }
        }

        downloadTcs.SetResult(false);
        if (song.Source is not (SourceIds.YouTube or SourceIds.Deezer or SourceIds.Qobuz or SourceIds.Spotify))
        {
            Log.Error("Track download failed because its provider is unsupported");
        }
        return string.Empty;
    }

    public async Task<string> DownloadObject(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback = default)
    {
        // Create file path
        bool createTrackSubfolder = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Subfolders);
        if (createTrackSubfolder)
        {
            var wildCardStr = await SettingsViewModel.GetSetting<string>(SettingsViewModel.SubfolderWildcard);

            try
            {
                string newDirectory = GetTrackSubfolderPath(song, directory, wildCardStr);
                if (!Directory.Exists(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);
                }
                directory = newDirectory;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not create track subfolder; using the original download directory");
            }
        }

        var path = await DownloadTrackInternal(song, directory, progress, callback);
        if (!string.IsNullOrEmpty(path))
        {
            await ReplayGainDownloads.TagTrackAsync(path);
        }
        return path;
    }

    public async Task<List<string>> DownloadAlbum(AlbumSearchObject album, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback = default)
    {
        var originalDirectory = directory;
        bool createAlbumSubfolder = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AlbumSubfolders);
        var createTrackSubfolder = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Subfolders);
        var trackWildcard = await SettingsViewModel.GetSetting<string>(SettingsViewModel.SubfolderWildcard);
        if (createAlbumSubfolder)
        {
            var wildCardStr = await SettingsViewModel.GetSetting<string>(SettingsViewModel.AlbumSubfolderWildcard);
            if (string.IsNullOrWhiteSpace(wildCardStr))
            {
                wildCardStr = string.IsNullOrWhiteSpace(album.Isrc) ? "{artist} - {title}" : "{artist} - {title} [{isrc}]";
            }
            var subfolderName = ApiHelper.GetSafeDirectoryPath(ApiHelper.EvaluateWildcard(album, wildCardStr));


            string newDirectory = Path.Combine(directory, subfolderName);
            if (!Directory.Exists(newDirectory))
            {
                Directory.CreateDirectory(newDirectory);
            } else
            {
                if (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false) // Do not overwrite
                {
                    throw new Exception($"Directory already exists: '{newDirectory}'");
                }
            }
            directory = newDirectory;
        }

        if (album.Source == SourceIds.Qobuz)
        {
            var fullAlbumObj = await QobuzApi.GetInternalAlbum(album.Id);
            album.TrackList = fullAlbumObj.Tracks.Items.Select(t => QobuzApi.ConvertSongSearchObject(t, fullAlbumObj.Artist?.Name)).ToList();
        }
        else if (album.Source == SourceIds.Deezer)
        {
            var jsonObj = await DeezerApi.restClient.FetchJsonElement("album/" + album.Id);
            var fullAlbumObj = DeezerApi.GetAlbumFromJsonElement(jsonObj);

            album.TrackList = [];
            foreach (var t in fullAlbumObj?.TrackList ?? [])
            {
                var track = await DeezerApi.GetTrack(t.Id);
                if (track != null)
                {
                    album.TrackList.Add(track);
                }
            }

            album.AdditionalFields = new Dictionary<string, object?>
            {
                ["cover_big"] = jsonObj.SafeGetString("cover_big"),
                ["cover_xl"] = jsonObj.SafeGetString("cover_xl")
            };
        }
        else if (album.Source == SourceIds.Spotify)
        {
            List<SongSearchObject> newList = [];
            foreach (var t in album.TrackList ?? [])
            {
                var songObj = await SpotifyApi.GetTrack(t.Id);
                if (songObj != null)
                {
                    newList.Add(songObj);
                }
            }
            album.TrackList = newList;
        }
        else if (album.Source == SourceIds.YouTube)
        {
            // Nothing needed
        } 

        if (album.TrackList == null)
        {
            return [];
        }

        var downloadPaths = new ConcurrentQueue<string>();

        bool allSuccess = true;
        ConversionUpdateCallback conversionCallback = (severity, song, location) =>
        {
            if (severity != StatusLevel.Success)
            {
                allSuccess = false;
            }
            callback?.Invoke(severity, song, location);
        };

        var allTasks = new List<Task>();
        var throttler = new SemaphoreSlim(initialCount: await SettingsViewModel.GetSetting<int?>(SettingsViewModel.ConversionThreads) ?? 1);

        foreach (var track in album.TrackList)
        {
            await throttler.WaitAsync();

            allTasks.Add(Task.Run(async () =>
            {
                try
                {
                    // Fill missing info
                    track.AlbumName = album.Title ?? album.AlbumName;

                    var downloadDirectory = directory;
                    if (!createAlbumSubfolder && createTrackSubfolder)
                    {
                        downloadDirectory = GetTrackSubfolderPath(track, originalDirectory, trackWildcard);
                        if (!Directory.Exists(downloadDirectory))
                        {
                            Directory.CreateDirectory(downloadDirectory);
                        }
                    }

                    // Download the track
                    var downloadPath = await DownloadTrackInternal(track, downloadDirectory, progress, conversionCallback);
                    if (File.Exists(downloadPath))
                    {
                        downloadPaths.Enqueue(downloadPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Album track could not be downloaded");
                }
                finally
                {
                    // Release the semaphore slot
                    throttler.Release();
                }
            }));
        }
        await Task.WhenAll(allTasks);

        // The album's tracks are tagged together, so album gain can use all of them. TrackList can be shorter than
        // the album when a track couldn't be looked up, so the album's own track count decides if it's complete.
        var albumTrackCount = Math.Max(album.TracksCount, album.TrackList.Count);
        await ReplayGainDownloads.TagAlbumAsync(downloadPaths.ToList(), complete: downloadPaths.Count >= albumTrackCount);

        // Download cover if subfolder, fire and forget
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () =>
        {
            var downloadCoverArt = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.DownloadAlbumCoverArt);
            if (!downloadCoverArt)
            {
                return;
            }

            var coverDirectories = createAlbumSubfolder
                ? [directory]
                : GetAlbumCoverDirectories(album, downloadPaths, originalDirectory, createTrackSubfolder);
            if (coverDirectories.Count == 0)
            {
                return;
            }

            byte[]? coverBytes = null;

            try
            {
                var albumUrl = await GetAlbumArtLargest(album);
                if (!string.IsNullOrWhiteSpace(albumUrl))
                {
                    coverBytes = await client.GetByteArrayAsync(albumUrl);
                }

                if (coverBytes != null)
                {
                    foreach (var coverDirectory in coverDirectories)
                    {
                        await File.WriteAllBytesAsync(Path.Combine(coverDirectory, "cover.jpg"), coverBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Album cover could not be downloaded");
            }
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        return downloadPaths.ToList();
    }

    public static async Task<string?> GetAlbumArtLargest(AlbumSearchObject? album)
    {
        if (album == null) return null;
        if (album.Source == SourceIds.Qobuz)
        {
            var fullAlbumObj = await QobuzApi.GetInternalAlbum(album.Id);
            if (!string.IsNullOrWhiteSpace(fullAlbumObj?.Image?.Mega))
            {
                return fullAlbumObj.Image.Mega;
            }
            if (!string.IsNullOrEmpty(fullAlbumObj?.Image?.Extralarge))
            {
                return fullAlbumObj.Image.Extralarge;
            }
            if (!string.IsNullOrEmpty(fullAlbumObj?.Image?.Large))
            {
                return fullAlbumObj.Image.Large;
            }
        } 
        else if (album.Source == SourceIds.Deezer)
        {
            if (!string.IsNullOrEmpty(album.AdditionalFields?.GetValueOrDefault("cover_xl") as string))
            {
                return album.AdditionalFields["cover_xl"] as string;
            }
            if (!string.IsNullOrEmpty(album.AdditionalFields?.GetValueOrDefault("cover_big") as string))
            {
                return album.AdditionalFields["cover_big"] as string;
            }
        }
        else if (album.Source == SourceIds.Spotify)
        {
            if (!string.IsNullOrWhiteSpace(album.AdditionalFields?.GetValueOrDefault("cover_max") as string))
            {
                return album.AdditionalFields["cover_max"] as string;
            }
        }
        else if (album.Source == SourceIds.YouTube)
        {
            return await YoutubeApi.GetMaxResThumbnail(album);
        }
        return null;
    }
}
