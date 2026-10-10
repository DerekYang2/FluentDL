using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
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

namespace FluentDL.Helpers;

internal class ApiHelper
{   
    private static HttpClient client = new HttpClient();
    private static HttpClient clientNoRedir = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, true);

    // The string helpers below live in FluentDL.Core.Matching. These wrappers keep existing callers unchanged.
    public static bool IsSubstring(string str, string str2) => FuzzyMatch.IsSubstring(str, str2);

    public static string PruneTitle(string title) => TitleNormalizer.CompactLegacy(title);

    public static string PruneFeaturing(string? title) => TextNormalization.PruneFeaturing(title);

    public static string PrunePunctuation(string str) => TextNormalization.PrunePunctuation(str);

    public static bool CloseMatch(string str1, string str2) => FuzzyMatch.CloseMatch(str1, str2);

    // Convert illegal filename characters to an underscore
    public static string GetSafeFilename(string filename)
    {
        HashSet<string> reservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON","PRN","AUX","NUL",
            "COM0","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT0","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };
        var result = filename.Trim().TrimEnd('.');
        var s = string.Join("_", result.Split(Path.GetInvalidFileNameChars()));
        if (reservedNames.Contains(s))
        {
            s = "_" + s;
        }
        if (string.IsNullOrWhiteSpace(s))
        {
            s = "_";
        }
        return s;
    }

    // For safe directory only (not directory and file)
    public static string GetSafeDirectoryPath(string filename)
    {
        var result = filename.Trim().Replace('/', '\\');

        // Split 
        var segments = result.Split('\\', StringSplitOptions.None);
        // Sanitize each segment
        var sanitizedSegments = segments
            .Select(GetSafeFilename)
            .ToList();
        return string.Join("\\", sanitizedSegments);
    }

    public static string RemoveExtension(string filename)
    {
        var idx = filename.LastIndexOf('.');
        if (idx == -1) return filename;
        var ext = filename[idx..].ToLower();

        if (!LocalExplorerViewModel.SupportedExtensions.Contains(ext))
        {
            return filename; // Not a valid extension
        }

        return filename[..idx];
    }

    public static string GetUrl(SongSearchObject song)
    {
        var source = SourceCatalog.Find(song.Source);
        if (source == null)
        {
            return string.Empty;
        }
        return song is AlbumSearchObject ? source.AlbumUrl(song.Id) : source.TrackUrl(song.Id);
    }

    public static string EvaluateWildcard(SongSearchObject song, string wildcard)
    {
        if (song == null) throw new ArgumentNullException(nameof(song));
        if (wildcard == null) return string.Empty;

        static string SanitizeForPath(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace('/', '_').Replace('\\', '_');
        }

        static string FirstArtist(string? artists)
        {
            if (string.IsNullOrWhiteSpace(artists)) return string.Empty;
            var first = artists.Split(',')[0].Trim();
            return SanitizeForPath(first);
        }

        var album = SanitizeForPath(song.AlbumName ?? string.Empty);
        var artistsAll = SanitizeForPath(song.Artists ?? string.Empty);
        var artistsSorted = SanitizeForPath(GetSortedArtists(song.Artists ?? string.Empty));
        var id = SanitizeForPath(song.Id ?? string.Empty);
        var isrc = SanitizeForPath(song.Isrc ?? string.Empty);
        var position = SanitizeForPath((song.TrackPosition?.ToString() ?? "1"));
        var date = SanitizeForPath(song.ReleaseDate ?? string.Empty);
        var yearStr = (song.ReleaseDate?.Length ?? 0) >= 4 ? SanitizeForPath(song.ReleaseDate!.Substring(0, 4)) : "YYYY";
        var title = SanitizeForPath(song.Title ?? string.Empty);
        var artistFirst = FirstArtist(song.Artists);

        return wildcard
            .Replace("{album}", album)
            .Replace("{artist}", artistFirst)
            .Replace("{artists}", artistsAll)
            .Replace("{artists_sorted}", artistsSorted)
            .Replace("{id}", id)
            .Replace("{isrc}", isrc)
            .Replace("{position}", position)
            .Replace("{date}", date)
            .Replace("{year}", yearStr)
            .Replace("{title}", title)
            .Trim();
    }

    private static string GetSortedArtists(string? artists)
    {
        if (string.IsNullOrWhiteSpace(artists))
        {
            return string.Empty;
        }

        var sortedArtists = artists
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(artist => artist, StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", sortedArtists);
    }


    public static int CalcLevenshteinDistance(string a, string b) => FuzzyMatch.LevenshteinDistance(a, b);

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
            Log.Warning(e, "Could not resolve a redirected provider link");
            return null;
        }
    }

    public static string RemoveDiacritics(string text) => TextNormalization.RemoveDiacritics(text);

    public static string EnforceAscii(string text) => TextNormalization.EnforceAscii(text);

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
    public static async Task DownloadFileAsync(string filePath, string downloadUrl, IProgress<ProgressData> progress)
    {
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead); 
        response.EnsureSuccessStatusCode(); 
        var contentLength = response.Content.Headers.ContentLength ?? 0;

        using Stream streamRead = await response.Content.ReadAsStreamAsync();
        await using FileStream streamWrite = System.IO.File.Create(filePath);
        long totalBytesRead = 0;
        var buffer = new byte[32768]; // 32KB buffer size
        var firstBufferRead = false;

        Stopwatch stopwatch = Stopwatch.StartNew();
        var lastElapsed = stopwatch.Elapsed;
        int bytesRead;

        // Progress identifier
        var uniqueId = DateTime.Now.Ticks;

        try
        {
            while ((bytesRead = await streamRead.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await streamWrite.WriteAsync(buffer.AsMemory(0, Math.Min(buffer.Length, bytesRead)));
                totalBytesRead += bytesRead;
                double? percent = contentLength > 0 ? (double)totalBytesRead / contentLength : null;

                var loopElapsed = stopwatch.Elapsed - lastElapsed;
                progress.Report(new ProgressData() { uniqueId = uniqueId, currentBytes = totalBytesRead, totalBytes = contentLength, bps = bytesRead / loopElapsed.TotalSeconds });
                lastElapsed = stopwatch.Elapsed;
            }
        } catch
        {
            throw;
        } finally
        {
            progress.Report(new ProgressData() { uniqueId = uniqueId, currentBytes = contentLength, totalBytes = contentLength, bps = 0 });
        }
    }
}