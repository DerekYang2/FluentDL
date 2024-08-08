using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ABI.Microsoft.UI.Xaml.Media.Imaging;
using FluentDL.Models;
using FluentDL.Services;
using BitmapImage = Microsoft.UI.Xaml.Media.Imaging.BitmapImage;

namespace FluentDL.Helpers;

internal class ApiHelper
{
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

    public static string GetUrl(SongSearchObject song)
    {
        var id = song.Id;
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

    public static async Task DownloadObject(SongSearchObject song, Windows.Storage.StorageFile? file)
    {
        if (file == null)
        {
            return;
        }

        var url = GetUrl(song);
        var directory = Path.GetDirectoryName(file.Path);
        var fileName = Path.GetFileNameWithoutExtension(file.Path);
        var flacLocation = Path.Combine(directory, fileName + ".flac");

        if (song.Source == "youtube")
        {
            await YoutubeApi.DownloadAudio(url, directory, fileName, d =>
            {
            });
            // convert opus to flac
            var opusLocation = Path.Combine(directory, fileName + ".opus");

            await FFmpegRunner.ConvertOpusToFlac(opusLocation); // Convert opus to flac
            await YoutubeApi.UpdateMetadata(flacLocation, song.Id);
        }

        if (song.Source == "deezer")
        {
            await DeezerApi.DownloadTrack(song, flacLocation);
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

    public static async Task<Uri> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, }, true);
        using var response = await client.GetAsync(uri, cancellationToken);

        return new Uri(response.Headers.GetValues("Location").First());
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
        using var client = new HttpClient();
        var byteArr = await client.GetByteArrayAsync(uri);
        var bitmapImage = new BitmapImage();
        using var stream = new MemoryStream(byteArr);
        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        return bitmapImage;
    }

    // For any file downloading with progress
    public static async Task DownloadFileAsync(string downloadUrl, string filePath)
    {
        var httpClient = new HttpClient();
        using (Stream streamToReadFrom = await httpClient.GetStreamAsync(downloadUrl))
        {
            using (FileStream streamToWriteTo = System.IO.File.Create(filePath))
            {
                long totalBytesRead = 0;
                byte[] buffer = new byte[131072]; // 128KB buffer size
                bool firstBufferRead = false;
                Stopwatch stopwatch = Stopwatch.StartNew();

                int bytesRead;
                while ((bytesRead = await streamToReadFrom.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Write only the minimum of buffer.Length and bytesRead bytes to the file
                    await streamToWriteTo.WriteAsync(buffer, 0, Math.Min(buffer.Length, bytesRead));

                    // Calculate download speed
                    totalBytesRead += bytesRead;
                    double speed = totalBytesRead / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds;

                    // Update with the current speed at download start and then max. every 500 ms
                    if (!firstBufferRead || stopwatch.ElapsedMilliseconds >= 500)
                    {
                        Debug.WriteLine($"Downloading... {speed:F3} MB/s");
                    }

                    firstBufferRead = true;
                }
            }
        }
    }
}