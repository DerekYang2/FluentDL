using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using FluentDL.Helpers;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Org.BouncyCastle.Bcpg.OpenPgp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Serilog;

namespace FluentDL.Services;

internal class FFmpegRunner
{
    public static bool IsInitialized = false;

    private static int NormalizeSpectrogramHeight(int height)
    {
        return height switch
        {
            <= 384 => 256,
            <= 768 => 512,
            _ => 1024,
        };
    }

    public static async Task<string> GetPath()
    {
        var ffmpegPath = await SettingsViewModel.GetSetting<string?>(SettingsViewModel.FFmpegPath) ?? string.Empty;
        if (!Directory.Exists(ffmpegPath))
        {
            ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Assets\\ffmpeg\\bin");
        }
        return ffmpegPath;
    }

    public static async Task Initialize()
    {
        try
        {
            var ffmpegPath = await SettingsViewModel.GetSetting<string?>(SettingsViewModel.FFmpegPath) ?? string.Empty;
            var source = "custom folder";
            if (!Directory.Exists(ffmpegPath))
            {
                ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Assets\\ffmpeg\\bin");
                source = "bundled";
            }

            Log.Debug("Configuring FFmpeg");

            GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
            IsInitialized = true;
            // Not awaited, so a slow first start of ffmpeg doesn't hold up startup or the settings page.
            _ = Task.Run(() => LogVersionAsync(ffmpegPath, source));
        }
        catch (Exception e)
        {
            Log.Error(e, "FFmpeg configuration failed");
        }
    }

    // Starts ffmpeg once and logs its version, so the log shows whether conversions and spectrograms can work.
    // A failure is only logged, because FluentDL doesn't need ffmpeg until it converts audio or draws a spectrogram.
    private static async Task LogVersionAsync(string folder, string source)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(Path.Combine(folder, "ffmpeg.exe"), "-version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                },
            };
            process.Start();

            string output;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                Log.Warning("FFmpeg unavailable ({Source}): no answer within 30 seconds", source);
                return;
            }

            if (process.ExitCode != 0)
            {
                Log.Warning("FFmpeg unavailable ({Source}): exited with code {ExitCode}", source, process.ExitCode);
                return;
            }

            // The first line looks like "ffmpeg version N-116752-g507c2a577-2024-08-19-nonfree Copyright (c) ...".
            var match = Regex.Match(output, @"^ffmpeg version (\S+)");
            Log.Information("FFmpeg ready; version {Version} ({Source})", match.Success ? match.Groups[1].Value : "unknown", source);
        }
        catch (Exception e)
        {
            Log.Warning(e, "FFmpeg unavailable ({Source}); converting audio and spectrograms won't work", source);
        }
    }

    // Spectrogram
    public static async Task<BitmapImage?> GetSpectrogram(string filePath, int imageHeight, DispatcherQueue dispatcher)
    {
        try
        {
            int height = NormalizeSpectrogramHeight(imageHeight);
            int width = height * 2;
            using var ms = new MemoryStream();
            await FFMpegArguments.FromFileInput(filePath)
                .OutputToPipe(new StreamPipeSink(ms), options => options
                .WithCustomArgument("-filter_complex")
                .WithCustomArgument($"showspectrumpic=s={width}x{height}")
                .WithVideoCodec("png")
                .ForceFormat("image2pipe")).ProcessAsynchronously(throwOnError: true);
            ms.Position = 0;
            using IRandomAccessStream randomStream = ms.AsRandomAccessStream();
            
            var tcs = new TaskCompletionSource<BitmapImage?>();
            // UI Thread
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(randomStream);
                    tcs.SetResult(bitmap);
                }
                catch
                {
                    tcs.SetResult(null);
                }
            });

            return await tcs.Task;
        } catch (Exception ex)
        {
            Log.Warning(ex, "Spectrogram generation failed");
            return null;
        }
    }

    public static async Task<bool> SaveSpectrogram(string filePath, string outputPath, int imageHeight = 512)
    {
        try
        {
            int height = NormalizeSpectrogramHeight(imageHeight);
            int width = height * 2;

            await FFMpegArguments.FromFileInput(filePath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-filter_complex")
                    .WithCustomArgument($"showspectrumpic=s={width}x{height}")
                    .WithVideoCodec("png")
                    .ForceFormat("image2"))
                .ProcessAsynchronously(throwOnError: true);

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Spectrogram export failed");
            return false;
        }
    }

    /**
     * NOTE: this method removes the original file
     */
    public static async Task ConvertToFlacAsync(string inputPath, string outputPath)
    {
        await FFMpegArguments.FromFileInput(inputPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-c:a flac -map 0:a:0"))
            .ProcessAsynchronously();
        // Delete the original
        if (File.Exists(inputPath))
        {
            File.Delete(inputPath);
        }
    }

    public static async Task ConvertToFlacAsync(string initialPath, int samplingRate = 48000)
    {
        // Check if already flac
        if (initialPath.EndsWith(".flac"))
        {
            return;
        }

        string outputPath = ApiHelper.RemoveExtension(initialPath) + ".flac";

        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-sample_fmt s16")
                .WithAudioSamplingRate(samplingRate)).ProcessAsynchronously();

        // Delete the original
        if (File.Exists(outputPath) && File.Exists(initialPath))
        {
            File.Delete(initialPath);
        }
    }

    public static void ConvertToFlac(string initialPath, int samplingRate = 48000)
    {
        // Check if already flac
        if (initialPath.EndsWith(".flac"))
        {
            return;
        }

        string outputPath = ApiHelper.RemoveExtension(initialPath) + ".flac";

        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-sample_fmt s16")
                .WithAudioSamplingRate(samplingRate)).ProcessSynchronously();

        // Delete the original opus
        if (File.Exists(outputPath) && File.Exists(initialPath))
        {
            File.Delete(initialPath);
        }
    }

    public static async Task ConvertMp4ToM4aAsync(string inputPath, string outputPath, bool deleteOriginal = true)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("inputPath required", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath required", nameof(outputPath));
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Input file not found", inputPath);

        // Prevent same-path conflicts
        string fullInput = Path.GetFullPath(inputPath);
        string fullOutput = Path.GetFullPath(outputPath);

        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Input and output paths cannot be the same file.");
        }

        // Ensure target output directory exists
        string? outputDir = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        try
        {
            // Remux AAC stream from MP4 container directly to M4A container
            await FFMpegArguments
                .FromFileInput(fullInput)
                .OutputToFile(fullOutput, overwrite: true, options => options
                    .WithCustomArgument("-c copy -map 0:a:0"))
                .ProcessAsynchronously();

            if (!File.Exists(fullOutput) || new FileInfo(fullOutput).Length == 0)
            {
                throw new InvalidOperationException("Conversion failed: ffmpeg did not produce a valid output file.");
            }

            // Clean up the temporary input MP4 if requested
            if (deleteOriginal && File.Exists(fullInput))
            {
                File.Delete(fullInput);
            }
        }
        catch
        {
            // Clean up partial or corrupted output on failure
            if (File.Exists(fullOutput))
            {
                try { File.Delete(fullOutput); } catch { }
            }
            throw;
        }
    }

    public static async Task ConvertWebmToOpusAsync(string initialPath)
    {
        if (initialPath.EndsWith(".opus"))
        {
            return;
        }
        if (!initialPath.EndsWith(".webm"))
        {
            throw new ArgumentException("The file must be a webm file.");
        }

        // ffmpeg -i input.webm -c copy -map 0:a:0 output.opus
        var outputPath = ApiHelper.RemoveExtension(initialPath) + ".opus";

        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-c copy -map 0:a:0"))
            .ProcessAsynchronously();

        // Delete the original webm file
        if (File.Exists(initialPath))
        {
            File.Delete(initialPath);
        }
    }

    public static async Task ConvertWebmToFlacAsync(string initialPath)
    {
        if (!initialPath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The input file must be a .webm file.");
        }

        // Replace .webm extension with .flac
        var outputPath = ApiHelper.RemoveExtension(initialPath) + ".flac";

        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-c:a flac -map 0:a:0"))
            .ProcessAsynchronously();

        // Delete the original temporary .webm file
        if (File.Exists(initialPath))
        {
            File.Delete(initialPath);
        }
    }

    public static async Task RemuxOpusAsync(string inputPath, string outputPath, bool deleteOriginal = true)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("inputPath required", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath required", nameof(outputPath));
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Input file not found", inputPath);

        // Prevent same-path conflicts
        string fullInput = Path.GetFullPath(inputPath);
        string fullOutput = Path.GetFullPath(outputPath);

        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Input and output paths cannot be the same file.");
        }

        // Ensure output directory exists
        string? outputDir = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        try
        {
            // Remux directly from the temporary input to the final output path
            await FFMpegArguments
                .FromFileInput(fullInput)
                .OutputToFile(fullOutput, overwrite: true, options => options
                    .WithCustomArgument("-c copy"))
                .ProcessAsynchronously();

            if (!File.Exists(fullOutput) || new FileInfo(fullOutput).Length == 0)
            {
                throw new InvalidOperationException("Remux failed: ffmpeg did not produce a valid output file.");
            }

            // Clean up the temporary input file
            if (deleteOriginal && File.Exists(fullInput))
            {
                File.Delete(fullInput);
            }
        }
        catch
        {
            // If FFmpeg fails mid-process, clean up any partial/corrupted output file
            if (File.Exists(fullOutput))
            {
                try { File.Delete(fullOutput); } catch { }
            }
            throw;
        }
    }

    private static string GetOutputPath(string initialPath, string extension, string? outputDirectory = null)
    {
        var directory = outputDirectory ?? Path.GetDirectoryName(initialPath);
        var fileName = Path.GetFileNameWithoutExtension(initialPath);
        var ext = Path.GetExtension(initialPath);

        if (!LocalExplorerViewModel.SupportedExtensions.Contains(ext.ToLower()))  // Likely no extension (. inside name)
        {
            return initialPath; // Return the original path, do not convert
        }

        string outputPath;
        if (Path.IsPathRooted(initialPath)) // If the path is absolute
        {
            outputPath = Path.Combine(directory, fileName + extension);
        }
        else // If the path is relative
        {
            outputPath = fileName + extension;
        }

        return outputPath;
    }

    // Synchronous conversion methods ---------------------------------------------------

    public static string? CreateFlac(string initialPath, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".flac")
        {
            return initialPath;
        }

        var outputPath = GetOutputPath(initialPath, ".flac", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-c:v copy")
                    .WithCustomArgument("-map_metadata 0")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy (breaks on some conversions)
            try
            {
                FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithCustomArgument("-map_metadata 0")).ProcessSynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "FLAC");
                return null;
            }
        }
    }

    // Does not delete the original file, converts to mp3 (variable bit rate)
    public static string? CreateMp3(string initialPath, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".mp3")
        {
            return initialPath;
        }

        var outputPath = GetOutputPath(initialPath, ".mp3", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithCustomArgument("-q:a 0")
                    .WithCustomArgument("-c:v copy")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-id3v2_version 3")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy
            try
            {
                FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithAudioCodec(AudioCodec.LibMp3Lame)
                        .WithCustomArgument("-q:a 0")
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-id3v2_version 3")).ProcessSynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "MP3");
                return null;
            }
        }
    }

    // Convert to mp3 with cbr (constant bit rate)
    public static string? CreateMp3(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".mp3")
        {
            return initialPath;
        }

        var outputPath = GetOutputPath(initialPath, ".mp3", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithCustomArgument($"-b:a {bitRate}k")
                    .WithCustomArgument("-c:v copy")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-id3v2_version 3")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy
            try
            {
                FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithAudioCodec(AudioCodec.LibMp3Lame)
                        .WithCustomArgument($"-b:a {bitRate}k")
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-id3v2_version 3")).ProcessSynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "MP3");
                return null;
            }
        }
    }

    public static string? CreateAac(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".m4a")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".m4a", outputDirectory);

        try
        {
            // First try with AudioCodec.LibFdk_Aac (better quality). Don't add another -c:a here: ffmpeg only uses the last
            // one, and an extra "-c:a aac" once made this attempt use FFmpeg's own encoder instead of FDK.
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                    .WithAudioCodec(AudioCodec.LibFdk_Aac)
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Warning(e, "FDK-AAC conversion failed; trying the fallback encoder");
            // Try again with AudioCodec.Aac
            try
            {
                // Try again with AudioCodec.Aac
                FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-c:a aac")
                        .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "AAC");
                return null;
            }
        }
    }

    public static string? CreateAlac(string initialPath, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".m4a")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".m4a", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy").WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-c:a alac")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy
            try
            {
                FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options.WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-c:a alac")).ProcessSynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "ALAC");
                return null;
            }
        }
    }

    // Vorbis with variable bit rate
    public static string? CreateVorbisVBR(string initialPath, int qScale, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".ogg")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".ogg", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument($"-q:a {qScale}")
                    .WithCustomArgument("-map_metadata 0")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "Vorbis");
            return null;
        }
    }

    // Vorbis with constant bit rate
    public static string? CreateVorbis(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".ogg")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".ogg", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "Vorbis");
            return null;
        }
    }

    public static string? CreateOpus(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".ogg")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".ogg", outputDirectory);

        try
        {
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-c:a libopus")
                    .WithCustomArgument("-vbr on")
                    .WithCustomArgument("-frame_duration 60")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "Opus");
            return null;
        }
    }
    // Asynchronous conversion methods -----------------------------------------------------

    // Does not delete the original file, converts to mp3 (variable bit rate)
    public static async Task<string?> CreateMp3Async(string initialPath, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".mp3")
        {
            return initialPath;
        }

        var outputPath = GetOutputPath(initialPath, ".mp3", outputDirectory);

        try
        {
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithCustomArgument("-q:a 0")
                    .WithCustomArgument("-c:v copy")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy
            try
            {
                await FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithAudioCodec(AudioCodec.LibMp3Lame)
                        .WithCustomArgument("-q:a 0")
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "MP3");
                return null;
            }
        }
    }

    // Convert to mp3 with cbr (constant bit rate)
    public static async Task<string?> CreateMp3Async(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".mp3")
        {
            return initialPath;
        }

        var outputPath = GetOutputPath(initialPath, ".mp3", outputDirectory);

        try
        {
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithCustomArgument($"-b:a {bitRate}k")
                    .WithCustomArgument("-c:v copy")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            // Try without -c:v copy 
            try
            {
                await FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithAudioCodec(AudioCodec.LibMp3Lame)
                        .WithCustomArgument($"-b:a {bitRate}k")
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "MP3");
                return null;
            }
        }
    }

    public static async Task<string?> CreateAacAsync(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".m4a")
        {
            return null; // Cannot edit file in place (return null because m4a container can store different codecs)
        }

        var outputPath = GetOutputPath(initialPath, ".m4a", outputDirectory);

        try
        {
            // First try with AudioCodec.LibFdk_Aac (better quality). Don't add another -c:a here: ffmpeg only uses the last
            // one, and an extra "-c:a aac" once made this attempt use FFmpeg's own encoder instead of FDK.
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                    .WithAudioCodec(AudioCodec.LibFdk_Aac)
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Warning(e, "FDK-AAC conversion failed; trying the fallback encoder");
            // Try again with AudioCodec.Aac
            try
            {
                // Try again with AudioCodec.Aac
                await FFMpegArguments.FromFileInput(initialPath)
                    .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithCustomArgument("-map_metadata 0")
                        .WithCustomArgument("-c:a aac")
                        .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio conversion to {Format} failed", "AAC");
                return null;
            }
        }
    }

    public static async Task<string?> CreateAlacAsync(string initialPath, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".m4a")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".m4a", outputDirectory);

        try
        {
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy").WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-c:a alac")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "ALAC");
            return null;
        }
    }

    public static async Task<string?> CreateVorbisAsync(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".ogg")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".ogg", outputDirectory);

        try
        {
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "Vorbis");
            return null;
        }
    }

    public static async Task<string?> CreateOpusAsync(string initialPath, int bitRate, string? outputDirectory = null)
    {
        if (Path.GetExtension(initialPath) == ".ogg")
        {
            return null; // Cannot edit file in place
        }

        var outputPath = GetOutputPath(initialPath, ".ogg", outputDirectory);

        try
        {
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-c:a libopus")
                    .WithCustomArgument("-vbr on")
                    .WithCustomArgument("-frame_duration 60")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio conversion to {Format} failed", "Opus");
            return null;
        }
    }
}