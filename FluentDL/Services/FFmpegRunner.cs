using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FluentDL.ViewModels;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace FluentDL.Services;

internal class FFmpegRunner
{
    public static bool IsInitialized = false;

    public static async Task Initialize()
    {
        try
        {
            var ffmpegPath = await SettingsViewModel.GetSetting<string?>(SettingsViewModel.FFmpegPath) ?? string.Empty;
            if (!Directory.Exists(ffmpegPath))
            {
                ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Assets/ffmpeg/bin");
            }

            Debug.WriteLine("FFMPEG PATH: " + ffmpegPath);

            GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
            IsInitialized = true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }

    /**
     * NOTE: this method removes the original file
     */
    public static async Task ConvertToFlacAsync(string initialPath, int samplingRate = 48000)
    {
        // Check if already flac
        if (initialPath.EndsWith(".flac"))
        {
            return;
        }

        var directory = Path.GetDirectoryName(initialPath);
        var fileName = Path.GetFileNameWithoutExtension(initialPath);
        string outputPath;
        if (Path.IsPathRooted(initialPath)) // If the path is absolute
        {
            outputPath = Path.Combine(directory, fileName + ".flac");
        }
        else // If the path is relative
        {
            outputPath = fileName + ".flac";
        }

        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-sample_fmt s16")
                .WithAudioSamplingRate(samplingRate)).ProcessAsynchronously();

        // Delete the original opus
        File.Delete(initialPath);
    }

    public static void ConvertToFlac(string initialPath, int samplingRate = 48000)
    {
        // Check if already flac
        if (initialPath.EndsWith(".flac"))
        {
            return;
        }

        var directory = Path.GetDirectoryName(initialPath);
        var fileName = Path.GetFileNameWithoutExtension(initialPath);
        string outputPath;
        if (Path.IsPathRooted(initialPath)) // If the path is absolute
        {
            outputPath = Path.Combine(directory, fileName + ".flac");
        }
        else // If the path is relative
        {
            outputPath = fileName + ".flac";
        }

        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithCustomArgument("-sample_fmt s16")
                .WithAudioSamplingRate(samplingRate)).ProcessSynchronously();

        // Delete the original opus
        File.Delete(initialPath);
    }

    public static async Task ConvertMp4ToM4aAsync(string initialPath)
    {
        if (!initialPath.EndsWith(".mp4"))
        {
            throw new ArgumentException("The file must be an mp4 file.");
        }
        //ffmpeg -i input.mp4 -c copy -map 0:a:0 output.m4a

        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(initialPath.Replace(".mp4", ".m4a"), true, options => options
                .WithCustomArgument("-c copy -map 0:a:0")).ProcessAsynchronously();

        // Delete the original mp4
        File.Delete(initialPath);
    }

    private static string GetOutputPath(string initialPath, string extension, string? outputDirectory = null)
    {
        var directory = outputDirectory ?? Path.GetDirectoryName(initialPath);
        var fileName = Path.GetFileNameWithoutExtension(initialPath);

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
                Debug.WriteLine("CONVERSION TO FLAC FAILED: " + ex.Message);
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
                Debug.WriteLine("CONVERSION TO MP3 FAILED: " + ex.Message);
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
                Debug.WriteLine("CONVERSION TO MP3 FAILED: " + ex.Message);
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
            // First try with AudioCodec.LibFdk_Aac (better quality)
            FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                    .WithAudioCodec(AudioCodec.LibFdk_Aac)
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-c:a aac")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION USING FDK-AAC FAILED: " + e.Message);
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
                Debug.WriteLine("CONVERSION TO AAC FAILED: " + ex.Message);
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
                Debug.WriteLine("CONVERSION TO ALAC FAILED: " + ex.Message);
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
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument($"-q:a {qScale}")
                    .WithCustomArgument("-map_metadata 0")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO VORBIS FAILED: " + e.Message);
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
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO VORBIS FAILED: " + e.Message);
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
                    .WithCustomArgument("-c:a libopus")
                    .WithCustomArgument("-vbr on")
                    .WithCustomArgument("-frame_duration 60")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessSynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO OPUS FAILED: " + e.Message);
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
                Debug.WriteLine("CONVERSION TO MP3 FAILED: " + ex.Message);
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
                Debug.WriteLine("CONVERSION TO MP3 FAILED: " + ex.Message);
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
            // First try with AudioCodec.LibFdk_Aac (better quality)
            await FFMpegArguments.FromFileInput(initialPath)
                .OutputToFile(outputPath, true, options => options.WithCustomArgument("-c:v copy")
                    .WithAudioCodec(AudioCodec.LibFdk_Aac)
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument("-c:a aac")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO AAC FAILED: " + e.Message);
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
                Debug.WriteLine("CONVERSION TO AAC FAILED: " + ex.Message);
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
            Debug.WriteLine("CONVERSION TO ALAC FAILED: " + e.Message);
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
                    .WithCustomArgument("-c:a libvorbis")
                    .WithCustomArgument("-map_metadata 0")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO VORBIS FAILED: " + e.Message);
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
                    .WithCustomArgument("-c:a libopus")
                    .WithCustomArgument("-vbr on")
                    .WithCustomArgument("-frame_duration 60")
                    .WithCustomArgument($"-b:a {bitRate}k")).ProcessAsynchronously();
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine("CONVERSION TO OPUS FAILED: " + e.Message);
            return null;
        }
    }
}