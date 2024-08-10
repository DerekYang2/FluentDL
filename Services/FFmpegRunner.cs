using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;

namespace FluentDL.Services;

internal class FFmpegRunner
{
    public static void Initialize()
    {
        GlobalFFOptions.Configure(options => options.BinaryFolder = Path.Combine(AppContext.BaseDirectory, "Assets/ffmpeg/bin"));
    }

    /**
     * NOTE: this method removes the original file
     */
    public static async Task ConvertToFlac(string initialPath, int samplingRate = 48000)
    {
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

    // Does not delete the original file
    public static void CreateMp3FromFlac(string initialPath)
    {
        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(initialPath.Replace(".flac", ".mp3"), true, options => options
                .WithAudioCodec(AudioCodec.LibMp3Lame).WithCustomArgument("-q:a 0").WithCustomArgument("-c:v copy")).ProcessSynchronously();
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