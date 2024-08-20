using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FluentDL.ViewModels;

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
    public static async Task ConvertToFlac(string initialPath, int samplingRate = 48000)
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

    public static async Task ConvertMP4toM4A(string initialPath)
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

    // Does not delete the original file, converts to mp3 (variable bit rate)
    public static async Task ConvertToMp3(string initialPath)
    {
        var lastIndex = initialPath.LastIndexOf('.');
        var outputPath = initialPath.Substring(0, lastIndex) + ".mp3";
        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithAudioCodec(AudioCodec.LibMp3Lame)
                .WithCustomArgument("-q:a 0")
                .WithCustomArgument("-c:v copy")
                .WithCustomArgument("-map_metadata 0")
                .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
    }

    // Convert to mp3 with cbr (constant bit rate)
    public static async Task ConvertToMp3(string initialPath, int bitRate)
    {
        var lastIndex = initialPath.LastIndexOf('.');
        var outputPath = initialPath.Substring(0, lastIndex) + ".mp3";
        await FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(outputPath, true, options => options
                .WithAudioCodec(AudioCodec.LibMp3Lame)
                .WithCustomArgument($"-b:a {bitRate}k")
                .WithCustomArgument("-c:v copy")
                .WithCustomArgument("-map_metadata 0")
                .WithCustomArgument("-id3v2_version 3")).ProcessAsynchronously();
    }

    public static void CreateAACFromFlac(string initialPath)
    {
        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(initialPath.Replace(".flac", ".aac"), true, options => options
                .WithCustomArgument("-b:a 256k")).ProcessSynchronously();
    }

    public static void CreateALACFromFLAC(string initialPath)
    {
        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(initialPath.Replace(".flac", ".m4a"), true, options => options.WithCustomArgument("-c:v copy").WithCustomArgument("-c:a alac")
            ).ProcessSynchronously();
    }
}