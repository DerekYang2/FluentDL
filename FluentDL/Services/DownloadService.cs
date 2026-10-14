using FluentDL.Contracts.Sources;
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
public sealed class DownloadService(SourceRegistry sources, EquivalentDownloader equivalents, ILyricService lyricService,
    Tidal.TidalDownloadClient? tidalDownloads = null)
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

    private async Task DownloadLyricsAsync(SongSearchObject song, string directory, string fileName, TaskCompletionSource<bool>? downloadTcs = default)
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

    private async Task<string> DownloadTrackInternal(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback = default)
    {
        using var operation = LogContext.PushProperty("OperationId", Guid.NewGuid().ToString("N"));
        var timer = Stopwatch.StartNew();
        var trackProgress = new TrackDownloadProgress(progress);
        Log.Information("Track download started; provider {Provider}", song.Source);
        try
        {
            var path = await DownloadTrackCore(song, directory, trackProgress, callback);
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
        finally { trackProgress.Finish(); }
    }

    private async Task<string> DownloadTrackCore(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback)
    {
        // Spotify and YouTube search rows lack full metadata; TIDAL searches and playlists lack their track number.
        if (((song.Source == SourceIds.Spotify && (string.IsNullOrWhiteSpace(song.ReleaseDate) || string.IsNullOrWhiteSpace(song.TrackPosition)))
                || (song.Source == SourceIds.YouTube && string.IsNullOrWhiteSpace(song.ReleaseDate))
                || song.Source == SourceIds.Tidal)
            && sources.TryGet<ICatalogSource>(song.Source, out var catalog))
        {
            song = await catalog.GetTrackAsync(song.Id) ?? song;
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


        if (sources.TryGet<IDownloadSource>(song.Source, out var downloader))
        {
            try
            {
                // Deezer retries at 128 kbps when its quality setting fails on this account; the others ignore it.
                var resultPath = await downloader.DownloadAsync(song, locationNoExt, new DownloadRequest(Allow128Fallback: true), progress);
                if (sources.TryGet<ITagSource>(song.Source, out var tagger))
                {
                    await tagger.WriteTagsAsync(resultPath, song);
                }
                downloadTcs.SetResult(true);
                callback?.Invoke(StatusLevel.Success, song, resultPath); // Assume success
                return resultPath;
            }
            catch (Exception e)
            {
                callback?.Invoke(StatusLevel.Error, song, e.Message);
                Log.Error(e, "{Provider} download or post-processing failed", downloader.Descriptor.DisplayName);
            }
        }
        else if (sources.TryGet<ICatalogSource>(song.Source, out var catalogOnly)) // Spotify: download an equivalent from another source
        {
            try
            {
                // The opt-in TIDAL client returns null for signed-out accounts and previews.
                var resultPath = song.Source == SourceIds.Tidal && tidalDownloads != null
                    ? await tidalDownloads.TryDownloadAsync(song.Id, locationNoExt, progress) : null;
                var direct = resultPath != null;
                resultPath ??= await equivalents.DownloadAsync(locationNoExt, song, progress, false, callback);

                if (resultPath != null)
                {
                    if (sources.TryGet<ITagSource>(song.Source, out var tagger))
                    {
                        await tagger.WriteTagsAsync(resultPath, song);
                    }
                    downloadTcs.SetResult(true);
                    if (direct) callback?.Invoke(StatusLevel.Success, song, resultPath);
                    return resultPath;
                }

                callback?.Invoke(StatusLevel.Error, song); // Null - error
                Log.Error("{Provider} equivalent-track download returned no result", catalogOnly.Descriptor.DisplayName);
            } catch (Exception e)
            {
                Log.Error(e, "{Provider} equivalent-track download or post-processing failed", catalogOnly.Descriptor.DisplayName);
                callback?.Invoke(StatusLevel.Error, song, e.Message);
            }
        }
        else
        {
            Log.Error("Track download failed because its provider is unsupported");
        }

        downloadTcs.SetResult(false);
        return string.Empty;
    }

    public async Task<string> DownloadObject(SongSearchObject song, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback? callback = default)
    {
        // Resolve a lightweight YouTube row before folder templates as well as the filename use its year.
        if (song.Source == SourceIds.YouTube && string.IsNullOrWhiteSpace(song.ReleaseDate)
            && sources.TryGet<ICatalogSource>(song.Source, out var youtubeCatalog))
        {
            try { song = await youtubeCatalog.GetTrackAsync(song.Id) ?? throw new InvalidOperationException("YouTube track metadata could not be loaded."); }
            catch (Exception e)
            {
                Log.Error(e, "YouTube metadata lookup failed before download naming");
                callback?.Invoke(StatusLevel.Error, song, e.Message);
                return string.Empty;
            }
        }
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

        if (sources.TryGet<ICatalogSource>(album.Source, out var albumCatalog))
        {
            album.TrackList = await albumCatalog.GetAlbumTracksAsync(album);
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

    private async Task<string?> GetAlbumArtLargest(AlbumSearchObject? album)
    {
        if (album == null) return null;
        return sources.TryGet<ICatalogSource>(album.Source, out var catalog) ? await catalog.GetLargestCoverUrlAsync(album) : null;
    }
}
