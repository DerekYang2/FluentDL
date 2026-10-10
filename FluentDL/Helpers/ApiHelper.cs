using FluentDL.Core.Helpers;
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

    public static string RemoveExtension(string filename) => AudioFiles.RemoveExtension(filename);

    public static string GetUrl(SongSearchObject song) => SourceCatalog.UrlOf(song);

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

    public static Task<Uri?> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default) => WebDownloads.GetRedirectedUrlAsync(uri, cancellationToken);

    public static string RemoveDiacritics(string text) => TextNormalization.RemoveDiacritics(text);

    public static string EnforceAscii(string text) => TextNormalization.EnforceAscii(text);

    public static string FormatDateTimeOffset(DateTimeOffset? dateTimeOffset) => DisplayFormat.FormatDateTimeOffset(dateTimeOffset);

    // For any file downloading with progress
    public static Task DownloadFileAsync(string filePath, string downloadUrl, IProgress<ProgressData> progress) => WebDownloads.DownloadFileAsync(filePath, downloadUrl, progress);
}