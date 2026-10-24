using System.Security.Cryptography;
using FFMpegCore;
using FluentDL.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDL.Characterization.Tests;

[TestClass]
[DoNotParallelize]
public class LocalFormatConverterTests
{
    [TestMethod]
    public void NormalApplicationRegistrationResolvesTheRealConverter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalFormatConverter, LocalFormatConverter>();
        using var provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<LocalFormatConverter>(provider.GetRequiredService<ILocalFormatConverter>());
    }

    [TestMethod]
    [DataRow(0, 0, ".flac")]
    [DataRow(1, 0, ".mp3")]
    [DataRow(1, 4, ".mp3")]
    [DataRow(2, 0, ".m4a")]
    [DataRow(2, 3, ".m4a")]
    [DataRow(3, 0, ".m4a")]
    [DataRow(4, 0, ".ogg")]
    [DataRow(4, 5, ".ogg")]
    [DataRow(5, 2, ".ogg")]
    public void ExistingFormatChoicesProduceReadableFilesAndKeepTheInput(int format, int quality, string extension)
    {
        var executable = FindEncoder();
        var previous = GlobalFFOptions.Current.BinaryFolder;
        var folder = Path.Combine(Path.GetTempPath(), "FluentDL-LocalConversionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = Path.GetDirectoryName(executable)!);
            var input = Path.Combine(folder, "input.wav");
            WriteWave(input);
            var before = SHA256.HashData(File.ReadAllBytes(input));
            var result = new LocalFormatConverter().Convert(input, folder, format, quality);
            Assert.AreEqual(Path.Combine(folder, "input" + extension), result);
            Assert.IsTrue(File.Exists(result));
            using var encoded = TagLib.File.Create(result);
            Assert.IsTrue(encoded.Properties.Duration.TotalSeconds > 0.2);
            CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(input)));
        }
        finally
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = previous);
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static string FindEncoder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "FluentDL", "Assets", "ffmpeg", "win-x64", "ffmpeg.exe");
            if (File.Exists(path)) return path;
        }
        Assert.Inconclusive("The bundled x64 FFmpeg is required for the local conversion check.");
        throw new InvalidOperationException();
    }

    private static void WriteWave(string path)
    {
        const int rate = 48000, frames = rate / 2, size = frames * 2 * sizeof(short);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(size);
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)(6000 * Math.Sin(2 * Math.PI * 1000 * frame / rate));
            writer.Write(sample); writer.Write(sample);
        }
    }
}
