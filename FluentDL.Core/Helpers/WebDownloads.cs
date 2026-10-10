using System.Diagnostics;
using FluentDL.Core.Sources;
using Serilog;

namespace FluentDL.Core.Helpers;

/// <summary>HTTP helpers the sources use: following redirects, and downloading a file with progress. Moved from ApiHelper.</summary>
public static class WebDownloads
{
    private static HttpClient client = new HttpClient();
    private static HttpClient clientNoRedir = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, true);

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
