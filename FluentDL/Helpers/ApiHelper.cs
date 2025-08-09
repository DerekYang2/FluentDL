using ABI.Microsoft.UI.Xaml.Media.Imaging;
using AngleSharp.Dom;
using AngleSharp.Media.Dom;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using QobuzApiSharp.Models.Content;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BitmapImage = Microsoft.UI.Xaml.Media.Imaging.BitmapImage;

namespace FluentDL.Helpers;

internal class ApiHelper
{
    private static HttpClient client = new HttpClient();
    private static HttpClient clientNoRedir = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, true);

    public static bool IsSubstring(string str, string str2)
    {
        str = PrunePunctuation(str).ToLower();
        str2 = PrunePunctuation(str2).ToLower();
        // If either is empty, return false
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(str2))
        {
            return false;
        }

        return str.Contains(str2) || str2.Contains(str);
    }

    public static string PruneTitle(string title)
    {
        var titlePruned = title.ToLower().Trim();

        // Remove (feat. X) from the title
        var index = titlePruned.IndexOf("(feat.");
        if (index != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index);
            titlePruned = titlePruned.Remove(index, closingIndex - index + 1);
        }

        // Remove (ft. X) from the title
        var index2 = titlePruned.IndexOf("(ft.");
        if (index2 != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index2);
            titlePruned = titlePruned.Remove(index2, closingIndex - index2 + 1);
        }

        // Remove (with X) from the title
        var index3 = titlePruned.IndexOf("(with");
        if (index3 != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index3);
            titlePruned = titlePruned.Remove(index3, closingIndex - index3 + 1);
        }

        // Remove punctuation that may cause inconsistency
        titlePruned = titlePruned.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "").Replace("—", "").Replace("'", "").Replace("\"", "");

        // Remove non ascii and replaced accented with normal
        titlePruned = ApiHelper.EnforceAscii(titlePruned);
        return titlePruned.Trim();
    }

    public static string PrunePunctuation(string str)
    {
        // Remove all punctuation and whitespace from string, only include alphanumeric characters
        return Regex.Replace(str, @"[^a-zA-Z0-9]", string.Empty);
    }

    // Convert illegal filename characters to an underscore
    public static string GetSafeFilename(string filename)
    {
        var result = filename.Trim().TrimEnd('.');
        return string.Join("_", result.Split(Path.GetInvalidFileNameChars()));
    }

    public static string RemoveExtension(string filename)
    {
        var idx = filename.LastIndexOf('.');
        if (idx == -1) return filename;

        var ext = filename.Substring(idx);
        // Check if ext is a valid extension (only contains letters)

        if (ext.All(char.IsLetter)) // If it is a valid extension
        {
            return filename.Substring(0, idx); // Remove the extension
        }

        return filename; // Return the original filename
    }

    public static string GetUrl(SongSearchObject song)
    {
        var id = song.Id;
        if (song is AlbumSearchObject album)
        {
            // Get the url of the current object
            return song.Source switch
            {
                "deezer" => "https://www.deezer.com/album/" + id,
                "youtube" => "https://www.youtube.com/watch?v=" + id,
                "spotify" => "https://open.spotify.com/album/" + id,
                "qobuz" => "https://open.qobuz.com/album/" + id,
                "local" => id,
                _ => string.Empty
            };
        }
        else
        {
            // Get the url of the current object
            return song.Source switch
            {
                "deezer" => "https://www.deezer.com/track/" + id,
                "youtube" => "https://www.youtube.com/watch?v=" + id,
                "spotify" => "https://open.spotify.com/track/" + id,
                "qobuz" => "https://open.qobuz.com/track/" + id,
                "local" => id,
                _ => string.Empty
            };
        }
    }

    public static string EvaluateWildcard(SongSearchObject song, string wildcard)
    {
        // Replace the wildcard with the song's properties
        return wildcard
            .Replace("{album}", song.AlbumName)
            .Replace("{artist}", song.Artists.Split(",")[0].Trim()) // First artist
            .Replace("{artists}", song.Artists) // All artists
            .Replace("{id}", song.Id)
            .Replace("{isrc}", song.Isrc ?? string.Empty) // ISRC, if available
            .Replace("{position}", song.TrackPosition?.ToString() ?? "1")
            .Replace("{release_date}", song.ReleaseDate ?? "")
            .Replace("{title}", song.Title);
    }
   
    private static async Task<string> DownloadTrackInternal(SongSearchObject song, string directory, ConversionUpdateCallback? callback = default)
    {
        // Create file name
        var firstArtist = song.Artists.Split(",")[0].Trim();
        var isrcStr = !string.IsNullOrWhiteSpace(song.Isrc) ? $" [{song.Isrc}]" : "";
        var fileName = GetSafeFilename($"{song.TrackPosition}. {firstArtist} - {song.Title}{isrcStr}"); // File name no extension

        var locationNoExt = Path.Combine(directory, fileName);

        if (song.Source == "youtube")
        {
            var flacLocation = Path.Combine(directory, fileName + ".flac");
            var opusLocation = Path.Combine(directory, fileName + ".opus");
            var mp4Location = Path.Combine(directory, fileName + ".mp4");
            var m4aLocation = Path.Combine(directory, fileName + ".m4a");
            try
            {
                // 0 - opus
                // 1 - flac (opus)
                // 2 - m4a (aac)
                var settingIdx = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.YoutubeQuality) ?? 0;
                if (settingIdx == 2)
                {
                    if (File.Exists(mp4Location) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false) // Do not overwrite
                    {
                        throw new Exception("File already exists."); // Will be caught below
                    }

                    if (!FFmpegRunner.IsInitialized)
                    {
                        throw new Exception("FFmpeg is not initialized.");
                    }

                    await YoutubeApi.DownloadAudioAAC(mp4Location, song.Id);
                    await FFmpegRunner.ConvertMp4ToM4aAsync(mp4Location);
                    await YoutubeApi.UpdateMetadata(m4aLocation, song.Id);
                    callback?.Invoke(InfoBarSeverity.Success, song, m4aLocation); // Assume success
                    return m4aLocation;
                }


                if (File.Exists(opusLocation) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false) // Do not overwrite
                {
                    throw new Exception("File already exists."); // Will be caught below
                }

                await YoutubeApi.DownloadAudio(opusLocation, song.Id); // Download opus stream

                if (settingIdx == 0) // Do not convert to flac
                {
                    await YoutubeApi.UpdateMetadata(opusLocation, song.Id);
                    callback?.Invoke(InfoBarSeverity.Success, song, opusLocation); // Assume success
                    return opusLocation;
                }

                // Convert to flac
                if (!FFmpegRunner.IsInitialized)
                {
                    throw new Exception("FFmpeg is not initialized.");
                }

                await FFmpegRunner.ConvertToFlacAsync(opusLocation); // Convert opus to flac
                await YoutubeApi.UpdateMetadata(flacLocation, song.Id);
                callback?.Invoke(InfoBarSeverity.Success, song, flacLocation); // Assume success
                return flacLocation;
            }
            catch (Exception e)
            {
                callback?.Invoke(InfoBarSeverity.Error, song, e.Message);
                Debug.WriteLine("Failed to download song: " + e.Message);
            }
        }

        if (song.Source == "deezer")
        {
            try
            {
                var resultPath = await DeezerApi.DownloadTrack(locationNoExt, song);
                await DeezerApi.UpdateMetadata(resultPath, song.Id);
                callback?.Invoke(InfoBarSeverity.Success, song, resultPath); // Assume success
                return resultPath;
            }
            catch (Exception e)
            {
                callback?.Invoke(InfoBarSeverity.Error, song, e.Message);
                Debug.WriteLine("Failed to download song: " + e.Message);
            }
        }

        if (song.Source == "qobuz")
        {
            try
            {
                var resultPath = await QobuzApi.DownloadTrack(locationNoExt, song);
                await QobuzApi.UpdateMetadata(resultPath, song.Id);
                callback?.Invoke(InfoBarSeverity.Success, song, resultPath); // Assume success
                return resultPath;
            }
            catch (Exception e)
            {
                callback?.Invoke(InfoBarSeverity.Error, song, e.Message);
                Debug.WriteLine("Failed to download song: " + e.Message);
            }
        }

        if (song.Source == "spotify")
        {
            var resultPath = await SpotifyApi.DownloadEquivalentTrack(locationNoExt, song, true, callback);

            if (resultPath != null)
            {
                await SpotifyApi.UpdateMetadata(resultPath, song.Id);
                return resultPath;
            }

            callback?.Invoke(InfoBarSeverity.Error, song); // Null - error
        }

        return string.Empty;
    }

    public static async Task<string> DownloadObject(SongSearchObject song, string directory, ConversionUpdateCallback? callback = default)
    {
        // Create file path
        bool createTrackSubfolder = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Subfolders);
        if (createTrackSubfolder)
        {
            var wildCardStr = await SettingsViewModel.GetSetting<string>(SettingsViewModel.SubfolderWildcard);
            if (string.IsNullOrWhiteSpace(wildCardStr))
            {
                wildCardStr = string.IsNullOrWhiteSpace(song.Isrc) ? "{position}. {artist} - {title}" : "{position}. {artist} - {title} [{isrc}]";
            }
            var subfolderName = GetSafeFilename(EvaluateWildcard(song, wildCardStr));

            try
            {
                string newDirectory = Path.Combine(directory, subfolderName);
                if (!Directory.Exists(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);
                }
                directory = newDirectory;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error creating subfolder: " + ex.Message);
            }
        }

        return await DownloadTrackInternal(song, directory, callback);
    }

    public static async Task<List<string>> DownloadAlbum(AlbumSearchObject album, string directory, ConversionUpdateCallback? callback = default)
    {
        bool createAlbumSubfolder = await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AlbumSubfolders);
        if (createAlbumSubfolder)
        {
            var wildCardStr = await SettingsViewModel.GetSetting<string>(SettingsViewModel.SubfolderWildcard);
            if (string.IsNullOrWhiteSpace(wildCardStr))
            {
                wildCardStr = string.IsNullOrWhiteSpace(album.Isrc) ? "{artist} - {title}" : "{artist} - {title} [{isrc}]";
            }
            var subfolderName = GetSafeFilename(EvaluateWildcard(album, wildCardStr));

            try
            {
                string newDirectory = Path.Combine(directory, subfolderName);
                if (!Directory.Exists(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);
                }
                directory = newDirectory;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error creating subfolder: " + ex.Message);
            }
        }

        if (album.Source == "qobuz")
        {
            var fullAlbumObj = await QobuzApi.GetInternalAlbum(album.Id);
            album.TrackList = fullAlbumObj.Tracks.Items.Select(t => QobuzApi.ConvertSongSearchObject(t, fullAlbumObj.Artist?.Name)).ToList();
        }
        else if (album.Source == "deezer")
        {
            var jsonObj = await DeezerApi.restClient.FetchJsonElement("album/" + album.Id);
            var fullAlbumObj = DeezerApi.GetAlbumFromJsonElement(jsonObj);
            var trackTasks = fullAlbumObj?.TrackList?.Select(t => DeezerApi.GetTrack(t.Id));
            var resolvedTasks = await Task.WhenAll(trackTasks ?? []);
            album.TrackList = [.. resolvedTasks];
            album.AdditionalFields = new Dictionary<string, object?>
            {
                ["cover_big"] = jsonObj.SafeGetString("cover_big"),
                ["cover_xl"] = jsonObj.SafeGetString("cover_xl")
            };
        }

        if (album.TrackList == null)
        {
            return [];
        }

        List<string> downloadedFiles = [];
        bool allSuccess = true;
        ConversionUpdateCallback conversionCallback = (severity, song, location) =>
        {
            if (severity != InfoBarSeverity.Success)
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
                    // Download the track
                    var downloadPath = await DownloadTrackInternal(track, directory, conversionCallback);
                    if (File.Exists(downloadPath))
                    {
                        downloadedFiles.Add(downloadPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error downloading track: " + ex.Message);
                }
                finally
                {
                    // Release the semaphore slot
                    throttler.Release();
                }
            }));
        }
        await Task.WhenAll(allTasks);

        // Download cover if subfolder, fire and forget
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () =>
        {
            if (createAlbumSubfolder)
            {
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
                        await File.WriteAllBytesAsync(Path.Combine(directory, "cover.jpg"), coverBytes);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        return downloadedFiles;
    }

    public static async Task<string?> GetAlbumArtLargest(AlbumSearchObject? album)
    {
        if (album == null) return null;
        if (album.Source == "qobuz")
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
        else if (album.Source == "deezer")
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
        return null;
    }

    public static async Task DownloadObject(SongSearchObject song, Windows.Storage.StorageFile? file)
    {
        if (file == null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(file.Path);
        var fileName = Path.GetFileNameWithoutExtension(file.Path);
        var flacLocation = Path.Combine(directory, fileName + ".flac");
        var opusLocation = Path.Combine(directory, fileName + ".opus");

        if (song.Source == "youtube")
        {
            await YoutubeApi.DownloadAudio(opusLocation, song.Id);
            await FFmpegRunner.ConvertToFlacAsync(opusLocation); // Convert opus to flac
            await YoutubeApi.UpdateMetadata(flacLocation, song.Id);
        }

        if (song.Source == "deezer")
        {
            await DeezerApi.DownloadTrack(flacLocation, song);
            await DeezerApi.UpdateMetadata(flacLocation, song.Id);
        }

        if (song.Source == "qobuz")
        {
            await QobuzApi.DownloadTrack(flacLocation, song);
            await QobuzApi.UpdateMetadata(flacLocation, song.Id);
        }

        if (song.Source == "spotify")
        {
            await SpotifyApi.DownloadEquivalentTrack(flacLocation, song);
            await SpotifyApi.UpdateMetadata(flacLocation, song.Id);
        }
    }

    public static int CalcLevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(a))
        {
            return b.Length;
        }

        if (string.IsNullOrEmpty(b))
        {
            return a.Length;
        }

        int lengthA = a.Length;
        int lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (int i = 0; i <= lengthA; distances[i, 0] = i++) ;
        for (int j = 0; j <= lengthB; distances[0, j] = j++) ;

        for (int i = 1; i <= lengthA; i++)
        {
            for (int j = 1; j <= lengthB; j++)
            {
                int cost = b[j - 1] == a[i - 1] ? 0 : 1;

                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost
                );
            }
        }

        return distances[lengthA, lengthB];
    }

    public static async Task<Uri?> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        try
        {
            Uri prevUri;
            Uri curUri = uri;

            do
            {
                using var response = await clientNoRedir.GetAsync(curUri, cancellationToken);

                if (!response.Headers.Contains("Location"))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.OK && response.ReasonPhrase == "OK")
                    {
                        return curUri;  // No redirection, return the current URI
                    } 
                    else
                    {
                        return null;
                    }
                }

                prevUri = curUri;
                curUri = new Uri(response.Headers.GetValues("Location").First());
            } while (curUri.AbsoluteUri != prevUri.AbsoluteUri);

            return curUri;
        }
        catch (Exception e)
        {
            Debug.WriteLine("Error getting redirected URL: " + e.Message);
            return null;
        }
    }

    public static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    public static string EnforceAscii(string text)
    {
        var result = ApiHelper.RemoveDiacritics(text);
        result = Regex.Replace(result, @"[^\u0000-\u007F]+", string.Empty);
        return result;
    }

    public static string FormatDateTimeOffset(DateTimeOffset? dateTimeOffset)
    {
        if (dateTimeOffset != null)
        {
            return dateTimeOffset.GetValueOrDefault().ToString("yyyy-MM-dd");
        }
        else
        {
            return "";
        }
    }

    // Gets a bitmap image from a URL
    public static async Task<BitmapImage> GetBitmapImageAsync(string uri)
    {
        var byteArr = await client.GetByteArrayAsync(uri);
        var bitmapImage = new BitmapImage();
        using var stream = new MemoryStream(byteArr);
        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        return bitmapImage;
    }

    // For any file downloading with progress
    public static async Task DownloadFileAsync(string filePath, string downloadUrl)
    {
        await using Stream streamRead = await client.GetStreamAsync(downloadUrl);
        await using FileStream streamWrite = System.IO.File.Create(filePath);
        var totalBytesRead = 0;
        var buffer = new byte[131072]; // 128KB buffer size
        var firstBufferRead = false;
        //Stopwatch stopwatch = Stopwatch.StartNew();

        int bytesRead;
        while ((bytesRead = await streamRead.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await streamWrite.WriteAsync(buffer.AsMemory(0, Math.Min(buffer.Length, bytesRead)));
            /*
            totalBytesRead += bytesRead;
            
            var speed = totalBytesRead / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds;

            if (!firstBufferRead || stopwatch.ElapsedMilliseconds >= 500)
            {
                Debug.WriteLine($"Downloading... {speed:F3} MB/s");
            }

            firstBufferRead = true;
            */
        }
    }
}