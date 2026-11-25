using System.Diagnostics;
using System.Text;

namespace FluentDL.Services
{
    /// <summary>
    /// Runs yt-dlp: downloads YouTube audio with it, and checks or updates the executable. App tooling used by YouTube
    /// downloads, the preview pane and the Settings page; moved out of YoutubeApi unchanged.
    /// </summary>
    internal static class YtDlp
    {
        internal const string AacAudioFormat = "bestaudio[acodec=mp4a.40.2]/bestaudio[acodec^=mp4a]";

        internal static string PreferredAudioFormat(bool worst)
        {
            var audio = worst ? "worstaudio" : "bestaudio";
            return $"{audio}[acodec=opus]/{audio}[acodec^=opus]/{audio}[acodec^=mp4a]";
        }

        public static Task<(string FilePath, bool IsOpus)> DownloadAudioYTDLP(
            string fileStem,
            string youtubeUrl,
            string? executablePath,
            string? ffmpegDirectory,
            CancellationToken cancellationToken = default) =>
            DownloadAudioYTDLPInternal(fileStem, youtubeUrl, executablePath, ffmpegDirectory, PreferredAudioFormat(worst: false), cancellationToken);

        public static async Task<string> DownloadAudioYTDLPWorst(
            string fileStem,
            string youtubeUrl,
            string? executablePath,
            string? ffmpegDirectory,
            CancellationToken cancellationToken = default) =>
            (await DownloadAudioYTDLPInternal(fileStem, youtubeUrl, executablePath, ffmpegDirectory, PreferredAudioFormat(worst: true), cancellationToken)).FilePath;

        public static async Task<string> DownloadAudioAACYTDLP(
            string fileStem,
            string youtubeUrl,
            string? executablePath,
            string? ffmpegDirectory,
            CancellationToken cancellationToken = default) =>
            (await DownloadAudioYTDLPInternal(fileStem, youtubeUrl, executablePath, ffmpegDirectory, AacAudioFormat, cancellationToken)).FilePath;

        private static async Task<(string FilePath, bool IsOpus)> DownloadAudioYTDLPInternal(
            string fileStem,
            string youtubeUrl,
            string? executablePath,
            string? ffmpegDirectory,
            string format,
            CancellationToken cancellationToken = default)
        {
            var fullPath = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                ? executablePath
                : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets\\yt-dlp\\yt-dlp.exe");

            try
            {
                return await RunYtdlpAsync(fullPath, fileStem, youtubeUrl, ffmpegDirectory, format, cancellationToken);
            }
            catch
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(fileStem)!, Path.GetFileName(fileStem) + ".*"))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        private static async Task<(string FilePath, bool IsOpus)> RunYtdlpAsync(
            string executablePath,
            string fileStem,
            string youtubeUrl,
            string? ffmpegDirectory,
            string format,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("--encoding");
            startInfo.ArgumentList.Add("utf-8");
            if (Directory.Exists(ffmpegDirectory) && File.Exists(Path.Combine(ffmpegDirectory, "ffmpeg.exe")))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(ffmpegDirectory);
            }
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(format);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(fileStem.Replace("%", "%%") + ".%(ext)s");
            startInfo.ArgumentList.Add("--print");
            startInfo.ArgumentList.Add("after_move:%(acodec)s\t%(filepath)s");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(youtubeUrl);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error = await errorTask;
                if (process.ExitCode != 0)
                {
                    throw new Exception($"yt-dlp error: {error.Trim()}");
                }

                var result = output.TrimEnd('\r', '\n').Split('\t', 2);
                if (result.Length != 2 || !File.Exists(result[1]))
                {
                    throw new IOException("yt-dlp did not report a downloaded audio file.");
                }

                return (result[1], result[0].StartsWith("opus", StringComparison.OrdinalIgnoreCase));
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
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
