using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentDL.Services;

internal class FFmpegRunner
{
    public static void Run(string command)
    {
        Process process = new Process();
        process.StartInfo.FileName = "cmd.exe";
        process.StartInfo.WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Assets/ffmpeg/bin");
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();

        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
        process.StandardInput.Close();
        process.WaitForExit();
        Debug.WriteLine(process.StandardOutput.ReadToEnd());
    }

    public static void ConvertOpusToFlac(string initialPath)
    {
        // Extra flags for ffmpeg: -sample_fmt s16 -ar 44100 -compression_level 10
        var command = $"ffmpeg -i \"{initialPath}\" -compression_level 10 -sample_fmt s16 -ar 44100 \"{initialPath.Replace(".opus", ".flac")}\"";
        Run(command);
    }
}