using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Download behaviour that DownloadTests (default settings, no FFmpeg) doesn't reach: Deezer's lossless request with
/// and without FFmpeg, and the settings for file names, subfolders, overwriting and album folders. Each test changes
/// only the settings it names and puts them back afterwards. Free test accounts: Deezer serves 128 kbps MP3s.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class DownloadSettingsTests
{
    private static readonly Fixtures.Track GetLucky = Fixtures.Tracks[0];
    private static readonly Fixtures.Track Halo = Fixtures.Tracks[2];

    // A Deezer account that reports lossless but can't get it (the free test account, told it is premium) asks for
    // FLAC, falls back to 128 kbps, and FFmpeg converts the MP3 to a 44.1 kHz FLAC.
    [TestMethod]
    public async Task LosslessFallbackIsConvertedToFlacWithFfmpeg()
    {
        var ffmpegFolder = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .FirstOrDefault(folder => File.Exists(Path.Combine(folder, "ffmpeg.exe")));
        if (ffmpegFolder == null) Assert.Inconclusive("ffmpeg.exe is not on PATH.");
        await TestSources.Settings.SaveSettingAsync(SettingsViewModel.FFmpegPath, ffmpegFolder);
        await FFmpegRunner.Initialize();

        var deezer = await SignedInDeezer(new FFmpegRunner());
        var song = await deezer.GetTrack(Halo.DeezerId!);

        await InTempFolder(async folder =>
        {
            var path = await deezer.DownloadTrack(Path.Combine(folder, "halo"), song, DeezNET.Data.Bitrate.FLAC, use128Fallback: true);

            Assert.AreEqual(Path.Combine(folder, "halo.flac"), path);
            CollectionAssert.AreEqual(new[] { "halo.flac" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
            using var file = TagLib.File.Create(path);
            Assert.IsInstanceOfType(file, typeof(TagLib.Flac.File));
            Assert.AreEqual(44100, file.Properties.AudioSampleRate);
            Assert.AreEqual(double.Parse(song!.Duration), file.Properties.Duration.TotalSeconds, 2);
        });
    }

    // Without FFmpeg the same request can't produce a FLAC, so it fails instead of saving the MP3 as FLAC. That it fails
    // also shows the FLAC request itself fails on this account, so the test above really goes through the fallback.
    [TestMethod]
    public async Task LosslessFallbackWithoutFfmpegFails()
    {
        var deezer = await SignedInDeezer(TestSources.AudioConverter);
        var song = await deezer.GetTrack(Halo.DeezerId!);

        await InTempFolder(async folder =>
        {
            await Assert.ThrowsExceptionAsync<Exception>(() => deezer.DownloadTrack(Path.Combine(folder, "halo"), song, DeezNET.Data.Bitrate.FLAC, use128Fallback: true));
            Assert.AreEqual(0, Directory.GetFiles(folder).Length);
        });
    }

    [TestMethod]
    public async Task FileNameAndTrackSubfolderFollowTheirPatterns()
    {
        await LiveSources.EnsureDeezerAsync();
        var song = await TestSources.Deezer.GetTrack(GetLucky.DeezerId!);

        await WithSettings(new() { ["file_naming_wildcard"] = "{artist} - {title}", ["subfolders"] = true, ["subfolder_wildcard"] = @"{artist}\{album} ({year})" }, () =>
            InTempFolder(async folder =>
            {
                var path = await TestSources.Downloads.DownloadObject(song!, folder, new Progress<ProgressData>(_ => { }));

                Assert.AreEqual(Path.Combine(folder, "Daft Punk", "Random Access Memories (2013)", "Daft Punk - Get Lucky (feat. Pharrell Williams and Nile Rodgers).mp3"), path);
                Assert.IsTrue(File.Exists(path));
            }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OverwriteSettingDecidesASecondDownload(bool overwrite)
    {
        await LiveSources.EnsureDeezerAsync();
        var song = await TestSources.Deezer.GetTrack(Halo.DeezerId!);

        await WithSettings(new() { [SettingKeys.Overwrite] = overwrite }, () =>
            InTempFolder(async folder =>
            {
                var first = await TestSources.Downloads.DownloadObject(song!, folder, new Progress<ProgressData>(_ => { }));
                File.WriteAllBytes(first, [1, 2, 3]); // so a second download shows whether it replaced the file

                var reports = new List<(StatusLevel Level, string? Location)>();
                var second = await TestSources.Downloads.DownloadObject(song!, folder, new Progress<ProgressData>(_ => { }), (level, _, location) => reports.Add((level, location)));

                if (overwrite)
                {
                    Assert.AreEqual(first, second);
                    Assert.IsTrue(new FileInfo(first).Length > 1_000_000);
                    Assert.AreEqual(StatusLevel.Success, reports.Single().Level);
                }
                else
                {
                    Assert.AreEqual(string.Empty, second);
                    Assert.AreEqual(3, new FileInfo(first).Length);
                    Assert.AreEqual((StatusLevel.Error, "File already exists"), reports.Single());
                }
            }));
    }

    // With album subfolders on, an existing album folder stops the download unless overwriting is on. No download
    // happens, so this is quick.
    [TestMethod]
    public async Task ExistingAlbumFolderIsNotOverwritten()
    {
        await LiveSources.EnsureQobuzAsync();
        var items = new List<SongSearchObject>();
        await TestSources.Qobuz.AddFromLinkAsync(items, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
        var album = (AlbumSearchObject)items.Single();

        await WithSettings(new() { [SettingKeys.Overwrite] = false, ["album_subfolders"] = true, ["album_subfolder_wildcard"] = "{album}" }, () =>
            InTempFolder(async folder =>
            {
                Directory.CreateDirectory(Path.Combine(folder, "Random Access Memories"));

                var error = await Assert.ThrowsExceptionAsync<Exception>(() => TestSources.Downloads.DownloadAlbum(album, folder, new Progress<ProgressData>(_ => { })));

                StringAssert.StartsWith(error.Message, "Directory already exists");
                Assert.AreEqual(0, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
            }));
    }

    private static async Task<DeezerSource> SignedInDeezer(IAudioConverter converter)
    {
        var deezer = new DeezerSource(TestSources.Settings, converter);
        await deezer.ConnectAsync();
        if (!deezer.IsInitialized) Assert.Inconclusive("Deezer did not sign in with the ARL from the secrets file.");
        deezer.IsPremium = true; // ask for FLAC; the free account can't get it, so the 128 kbps fallback runs
        return deezer;
    }

    private static async Task WithSettings(Dictionary<string, object?> values, Func<Task> body)
    {
        var saved = new Dictionary<string, object?>();
        foreach (var (key, value) in values)
        {
            saved[key] = await TestSources.Settings.ReadSettingAsync<object>(key);
            await TestSources.Settings.SaveSettingAsync(key, value);
        }
        try
        {
            await body();
        }
        finally
        {
            foreach (var (key, value) in saved)
            {
                await TestSources.Settings.SaveSettingAsync(key, value);
            }
        }
    }

    private static async Task InTempFolder(Func<string, Task> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await body(folder);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
