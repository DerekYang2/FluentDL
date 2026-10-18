using System.Diagnostics;
using System.Text.RegularExpressions;
using FFMpegCore;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
[DoNotParallelize]
public class AacEncodingTests
{
    private static string previousBinaryFolder = "";

    [ClassInitialize]
    public static void ConfigureBundledEncoder(TestContext context)
    {
        previousBinaryFolder = GlobalFFOptions.Current.BinaryFolder;
        GlobalFFOptions.Configure(options => options.BinaryFolder = Path.GetDirectoryName(FindBundledFfmpeg())!);
    }

    [ClassCleanup]
    public static void RestoreEncoderConfiguration() =>
        GlobalFFOptions.Configure(options => options.BinaryFolder = previousBinaryFolder);

    [TestMethod]
    [DataRow(128, false)]
    [DataRow(192, false)]
    [DataRow(256, true)]
    [DataRow(320, true)]
    public void FdkOptionsWidenOnlyHighBitrates(int bitrate, bool widened)
    {
        var input = Path.GetTempFileName();
        try
        {
            var arguments = FFmpegRunner.CreateFdkAacConversion(input, input + ".m4a", bitrate).Arguments;
            StringAssert.Contains(arguments, "libfdk_aac");
            Assert.AreEqual(1, Regex.Matches(arguments, @"(?:^|\s)-c:a\s").Count);
            StringAssert.Contains(arguments, $"-b:a {bitrate}k");
            StringAssert.Contains(arguments, "-map_metadata 0");
            Assert.AreEqual(widened, arguments.Contains("-cutoff 20000"));
            Assert.IsFalse(arguments.Contains("-ar "), "Keep the source sample rate.");
        }
        finally { File.Delete(input); }
    }

    [TestMethod]
    [DataRow(256)]
    [DataRow(320)]
    public async Task HighBitrateFdkRetainsA19KhzTone(int bitrate)
    {
        var executable = FindBundledFfmpeg();
        var directory = Path.Combine(Path.GetTempPath(), "FluentDL-AacTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "tones.wav");
            var output = Path.Combine(directory, "tones.m4a");
            WriteTones(input);
            var conversion = FFmpegRunner.CreateFdkAacConversion(input, output, bitrate)
                .Configure(options => options.BinaryFolder = Path.GetDirectoryName(executable)!);
            Assert.IsTrue(await conversion.ProcessAsynchronously());

            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-nostdin", "-hide_banner", "-i", output, "-ac", "1", "-f", "f32le", "pipe:1" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pcm = new MemoryStream();
            try
            {
                var log = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardOutput.BaseStream.CopyToAsync(pcm, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(0, process.ExitCode, await log);
                StringAssert.Contains(await log, "aac (LC)");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }

            var bytes = pcm.ToArray();
            const int rate = 48000, samples = rate / 2;
            Assert.IsTrue(bytes.Length >= rate * sizeof(float));
            double real = 0, imaginary = 0;
            for (int i = 0; i < samples; i++)
            {
                float sample = BitConverter.ToSingle(bytes, (rate / 2 + i) * sizeof(float));
                double phase = 2 * Math.PI * 19000 * i / rate;
                real += sample * Math.Cos(phase);
                imaginary += sample * Math.Sin(phase);
            }
            double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples;
            Assert.IsTrue(amplitude > 0.04, $"19 kHz tone was attenuated too far: {amplitude:0.0000}");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static string FindBundledFfmpeg()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "FluentDL", "Assets", "ffmpeg", "win-x64", "ffmpeg.exe");
            if (File.Exists(path)) return path;
        }
        Assert.Inconclusive("Bundled x64 ffmpeg is required for the audio encoding check.");
        throw new InvalidOperationException();
    }

    private static void WriteTones(string path)
    {
        const int rate = 48000, frames = rate * 2, dataSize = frames * 2 * sizeof(short);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + dataSize); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(dataSize);
        for (int i = 0; i < frames; i++)
        {
            var sample = (short)(32767 * 0.15 * (Math.Sin(2 * Math.PI * 1000 * i / rate) + Math.Sin(2 * Math.PI * 19000 * i / rate)));
            writer.Write(sample); writer.Write(sample);
        }
    }
}
