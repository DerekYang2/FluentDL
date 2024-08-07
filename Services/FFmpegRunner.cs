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
    public static void ConvertOpusToFlac(string initialPath) // Good for converting youtube opus (16 bit, 48000 hz) to flac
    {
        FFMpegArguments.FromFileInput(initialPath)
            .OutputToFile(initialPath.Replace(".opus", ".flac"), true, options => options
                .WithCustomArgument("-sample_fmt s16")
                .WithCustomArgument("-ar 48000")).ProcessSynchronously();

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