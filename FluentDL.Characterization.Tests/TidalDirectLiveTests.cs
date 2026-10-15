using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Services;
using FluentDL.Services.Tidal;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("Download")]
[DoNotParallelize]
public class TidalDirectLiveTests
{
    [TestMethod]
    public async Task SavedDownloadSessionRestoresTheEmailAndChecksSubscription()
    {
        var path = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION for this manual check.");
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [SettingKeys.TidalDownloadSession] = Convert.ToBase64String(await File.ReadAllBytesAsync(path!)),
        });
        var client = new TidalDownloadClient(settings);
        var account = await client.GetAccountNameAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(account));
        StringAssert.Contains(account, "@");
        CollectionAssert.Contains(new[] { "Active", "Not active" }, await client.GetSubscriptionStatusAsync());
        // Account identities remain in the encrypted session and the local UI, not console output.
    }

    // Explicit opt-in. The file contains the DPAPI-protected download session, never a checked-in credential.
    [TestMethod]
    [DataRow("HI_RES_LOSSLESS", ".flac")]
    [DataRow("LOSSLESS", ".flac")]
    [DataRow("HIGH", ".m4a")]
    [DataRow("LOW", ".m4a")]
    public async Task DirectDownloadIsRemuxedAndTaggedThroughTheAppPipeline(string quality, string extension)
    {
        var sessionFile = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION");
        if (string.IsNullOrEmpty(sessionFile)) Assert.Inconclusive("Set FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION for this manual check.");
        var settings = TestSources.Settings;
        var keys = new[] { SettingKeys.TidalDownloadSession, SettingKeys.TidalDownloadQuality, SettingsViewModel.FFmpegPath };
        var previous = new Dictionary<string, object?>();
        foreach (var key in keys) previous[key] = await settings.ReadSettingAsync<object>(key);
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-direct-live-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, Convert.ToBase64String(await File.ReadAllBytesAsync(sessionFile!)));
            await settings.SaveSettingAsync(SettingKeys.TidalDownloadQuality, quality);
            var ffmpegFolder = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
                .FirstOrDefault(p => File.Exists(Path.Combine(p, "ffmpeg.exe")));
            Assert.IsNotNull(ffmpegFolder);
            await settings.SaveSettingAsync(SettingsViewModel.FFmpegPath, ffmpegFolder);
            SourceServices.Configure(settings, new FFmpegRunner());
            await FFmpegRunner.Initialize();
            await LiveSources.EnsureTidalAsync();
            var direct = new TidalDownloadClient(settings);
            var track = await TestSources.Tidal.GetTrackAsync("293301151");
            Assert.IsNotNull(track);
            // Other services stay signed out in this process. An equivalent fallback cannot satisfy this test.
            var downloads = new DownloadService(TestSources.Registry, new EquivalentDownloader(TestSources.Registry),
                new LRCLyricService(new HttpClient(), settings), direct);
            var reports = new List<StatusLevel>();
            var transfer = new RecordingProgress();
            var path = await downloads.DownloadObject(track, folder, transfer, (level, _, _) => reports.Add(level));
            Assert.IsFalse(string.IsNullOrEmpty(path), "Direct TIDAL download failed or returned only a preview.");
            using var file = TagLib.File.Create(path);
            Assert.AreEqual(extension, Path.GetExtension(path));
            // What each Settings label promises: LOSSLESS is CD quality, HI_RES the best the track has (24-bit here).
            Console.WriteLine($"{quality}: {Path.GetExtension(path)}, {file.Properties.BitsPerSample}-bit, {file.Properties.AudioSampleRate} Hz, {file.Properties.AudioBitrate} kbps");
            if (quality == "HI_RES_LOSSLESS") Assert.AreEqual(24, file.Properties.BitsPerSample);
            if (quality == "LOSSLESS")
            {
                Assert.AreEqual(16, file.Properties.BitsPerSample);
                Assert.AreEqual(44100, file.Properties.AudioSampleRate);
            }
            Assert.IsTrue(file.Properties.AudioSampleRate >= 44100);
            Assert.AreEqual(double.Parse(track.Duration), file.Properties.Duration.TotalSeconds, 2);
            Assert.AreEqual(track.Title, file.Tag.Title);
            Assert.AreEqual(track.Isrc, file.Tag.ISRC);
            Assert.IsTrue(file.Tag.Pictures.Length > 0);
            Assert.IsTrue(file.Tag.Track > 0);
            CollectionAssert.AreEqual(new[] { StatusLevel.Success }, reports);
            Assert.IsFalse(Directory.GetFiles(folder).Any(p => p.EndsWith(".mp4")));
            var updates = transfer.Reports.ToArray();
            Assert.AreEqual(1, updates.Select(p => p.uniqueId).Distinct().Count());
            Assert.IsTrue(updates.Any(p => p.currentBytes > 0 && p.percent < 1), "Progress must arrive before the download finishes.");
            Assert.AreEqual(1d, updates.Last().percent);
            Assert.IsTrue(updates.All(p => p.percent is >= 0 and <= 1));
        }
        finally
        {
            foreach (var (key, value) in previous) await settings.SaveSettingAsync(key, value);
            SourceServices.Configure(settings, TestSources.AudioConverter);
            Directory.Delete(folder, true);
        }
    }

    private sealed class RecordingProgress : IProgress<ProgressData>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<ProgressData> Reports = new();
        public void Report(ProgressData value) => Reports.Enqueue(value);
    }
}
