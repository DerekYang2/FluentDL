using FluentDL.Core.Sources;
using FluentDL.Core.Matching;
using FluentDL.Helpers;
using FluentDL.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VideoLibrary;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models.Info;
using YouTubeMusicAPI.Models.Search;

namespace FluentDL.Services
{
    /// <summary>
    /// Runs yt-dlp: downloads YouTube audio with it, and checks or updates the executable. App tooling used by YouTube
    /// downloads, the preview pane and the Settings page; moved out of YoutubeApi unchanged.
    /// </summary>
    internal static class YtDlp
    {
        public static async Task DownloadAudioYTDLP(
            string filePath,
            string youtubeUrl,
            string? executablePath = null,
            CancellationToken cancellationToken = default)
        {
            // YouTube native stream format: `.webm` (contains raw Opus codec)
            // Convert after download: `.webm` -> `.opus` or desired target
            await DownloadAudioYTDLPInternal(filePath, youtubeUrl, executablePath, "bestaudio[acodec=opus]", "bestaudio[acodec^=opus]", cancellationToken);
        }

        public static async Task DownloadAudioYTDLPWorst(
            string filePath,
            string youtubeUrl,
            string? executablePath = null,
            CancellationToken cancellationToken = default)
        {
            // YouTube native stream format: `.webm` (contains raw Opus codec)
            // Convert after download: `.webm` -> `.opus` or desired target
            await DownloadAudioYTDLPInternal(filePath, youtubeUrl, executablePath, "worstaudio[acodec=opus]", "worstaudio[acodec^=opus]", cancellationToken);
        }

        public static async Task DownloadAudioAACYTDLP(
            string filePath,
            string youtubeUrl,
            string? executablePath = null,
            CancellationToken cancellationToken = default)
        {
            // YouTube native stream format: `.m4a` (contains raw AAC codec / mp4a.40.2)
            // Convert after download: No conversion needed if saving as .m4a; purely container-bound AAC
            if (filePath.EndsWith(".mp4"))
            {
                filePath = filePath.Substring(0, filePath.Length - 4) + ".m4a";
            }

            await DownloadAudioYTDLPInternal(filePath, youtubeUrl, executablePath, "bestaudio[acodec=mp4a.40.2]", "bestaudio[acodec^=mp4a]", cancellationToken);
        }

        private static async Task DownloadAudioYTDLPInternal(
            string filePath,
            string youtubeUrl,
            string? executablePath,
            string format,
            string fallbackFormat,
            CancellationToken cancellationToken = default)
        {
            var fullPath = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                ? executablePath
                : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets\\yt-dlp\\yt-dlp.exe");

            try
            {
                await RunYtdlpAsync(fullPath, filePath, youtubeUrl, format, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception firstError)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    await RunYtdlpAsync(fullPath, filePath, youtubeUrl, fallbackFormat, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception fallbackError)
                {
                    throw new Exception($"yt-dlp failed with the requested format: {firstError.Message} Fallback failed: {fallbackError.Message}", fallbackError);
                }
            }
        }

        private static async Task RunYtdlpAsync(
            string executablePath,
            string filePath,
            string youtubeUrl,
            string format,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
            };

            // 1. Select the specific audio format stream
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(format);

            // 2. Output directly to file path without running any post-processors (-x and --audio-format removed)
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(filePath);

            startInfo.ArgumentList.Add(youtubeUrl);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"yt-dlp error: {error.Trim()}");
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                throw;
            }
        }

        public static async Task<string?> UpdateYtdlpAsync(string? executablePath = null, CancellationToken cancellationToken = default)
        {
            var fullPath = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                ? executablePath
                : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets\\yt-dlp\\yt-dlp.exe");

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"yt-dlp executable not found at: {fullPath}");
            }

            var startInfo = new ProcessStartInfo(fullPath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            // Pass -U flag to trigger update
            startInfo.ArgumentList.Add("-U");

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();

                // Read output concurrently to avoid deadlocks
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    throw new Exception($"yt-dlp update failed: {error.Trim()}");
                }

                Debug.WriteLine($"yt-dlp update output: {output.Trim()}");
                return output;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                throw;
            }
        }
        public static async Task<string?> GetYtdlpVersionAsync(string? executablePath = null, CancellationToken cancellationToken = default)
        {
            string fullPath = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                ? executablePath
                : Path.Combine(AppContext.BaseDirectory, "Assets\\yt-dlp\\yt-dlp.exe");

            if (!File.Exists(fullPath))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo(fullPath)
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("--version");

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0)
                {
                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to get yt-dlp version: " + ex.Message);
            }
            return null;
        }
    }
}
