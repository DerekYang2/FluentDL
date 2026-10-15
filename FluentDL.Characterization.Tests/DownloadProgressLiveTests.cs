using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Records the progress that the Search page would show during real downloads: one Deezer album and one YouTube
/// track. Each update's bar value must match its text, never go backwards, move before the end, and reach 100%.
/// </summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("Download")]
[DoNotParallelize]
public class DownloadProgressLiveTests
{
    [TestMethod]
    public async Task DeezerAlbumProgressMovesBeforeTheLastTrack()
    {
        await LiveSources.EnsureDeezerAsync();
        var items = new List<SongSearchObject>();
        await TestSources.Deezer.AddFromLinkAsync(items, $"https://www.deezer.com/album/{Fixtures.DeezerAlbumId}",
            new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
        var album = (AlbumSearchObject)items.Single();
        var timeline = await Record(album.TracksCount, (folder, progress) => TestSources.Downloads.DownloadAlbum(album, folder, progress));
        // Deezer reports nothing until each file has arrived. Progress must still move between those reports.
        Assert.IsTrue(timeline.Select(u => u.Value).Distinct().Count() > album.TracksCount / 2 + 3, "Too few distinct steps.");
    }

    [TestMethod]
    public async Task QobuzAlbumProgressFollowsTheTransfer()
    {
        await LiveSources.EnsureQobuzAsync();
        var items = new List<SongSearchObject>();
        await TestSources.Qobuz.AddFromLinkAsync(items, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}",
            new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
        var album = (AlbumSearchObject)items.Single();
        await Record(album.TracksCount, (folder, progress) => TestSources.Downloads.DownloadAlbum(album, folder, progress));
    }

    [TestMethod]
    public async Task YoutubeTrackShowsProgressThroughLookupDownloadAndConversion()
    {
        var ffmpegFolder = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .FirstOrDefault(folder => File.Exists(Path.Combine(folder, "ffmpeg.exe")));
        if (ffmpegFolder == null) Assert.Inconclusive("ffmpeg.exe is not on PATH.");
        await TestSources.Settings.SaveSettingAsync(SettingsViewModel.FFmpegPath, ffmpegFolder);
        await FFmpegRunner.Initialize();
        await LiveSources.EnsureYoutubeMusicAvailableAsync();
        var youtube = new YoutubeSource(TestSources.Settings, new FFmpegRunner());
        var registry = new SourceRegistry([youtube]);
        var downloads = new DownloadService(registry, new EquivalentDownloader(registry), new LRCLyricService(new HttpClient(), TestSources.Settings));
        // A search row, as on the Search page: the download looks the track up first.
        var row = new SongSearchObject { Source = "youtube", Id = Fixtures.Tracks[1].YoutubeId!, Title = "Get Lucky", Artists = "Daft Punk" };
        var timeline = await Record(1, async (folder, progress) => [await downloads.DownloadObject(row, folder, progress)]);
        Assert.IsTrue(timeline.Any(u => u.Value > 0 && u.Value < 1), "No progress was shown before completion.");
    }

    private static async Task<List<(TimeSpan Time, double Value)>> Record(int tracks,
        Func<string, IProgress<ProgressData>, Task<List<string>>> download)
    {
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "progress-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var helper = new DownloadProgressHelper("Test", tracks);
        var timeline = new List<(TimeSpan, double)>();
        double preparingSeconds = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        helper.ProgressUpdated += (_, update) =>
        {
            if (update.Preparing)
            {
                Assert.AreEqual(0, update.ProgressValue);
                lock (timeline) preparingSeconds = clock.Elapsed.TotalSeconds;
                return;
            }
            var text = update.DisplayString.Split(" \u2022 ")[1].Split('%')[0].Trim('\u2007');
            Assert.AreEqual(update.ProgressValue, double.Parse(text) / 100, 0.00051, "Bar and text differ.");
            lock (timeline) timeline.Add((clock.Elapsed, update.ProgressValue));
        };
        // Report on this thread, like the page's Progress<T> does on the UI thread.
        var progress = new SyncProgress(helper.UpdateProgressLoop);
        helper.StartLoop();
        try
        {
            var paths = await download(folder, progress);
            Assert.IsTrue(paths.Count(p => File.Exists(p)) >= 1, "Nothing downloaded.");
            // The shown value glides to 100% after the last report; give it time.
            await Task.Delay(2500);
        }
        finally
        {
            helper.StopLoop();
            Directory.Delete(folder, true);
        }
        List<(TimeSpan Time, double Value)> copy;
        lock (timeline) copy = timeline.ToList();
        for (var i = 1; i < copy.Count; i++) Assert.IsTrue(copy[i].Value >= copy[i - 1].Value, "Progress went backwards.");
        Assert.AreEqual(1, copy.Last().Value, 0.000001, "Progress did not reach 100%.");
        // Measure until the bar first reaches 100%, not the extra wait after it.
        copy = copy.Take(copy.FindIndex(u => u.Value >= 1) + 1).ToList();
        var total = copy.Last().Time.TotalSeconds;
        var samples = Enumerable.Range(1, 9).Select(i => copy.LastOrDefault(u => u.Time.TotalSeconds <= total * i / 10).Value);
        // After Preparing, an ideal bar grows evenly with time. Report how far it strays from that and its largest jump.
        var start = copy.First().Time.TotalSeconds;
        var gap = copy.Max(u => Math.Abs(u.Value - (u.Time.TotalSeconds - start) / Math.Max(total - start, 0.001)));
        var jump = copy.Zip(copy.Skip(1), (a, b) => b.Value - a.Value).DefaultIfEmpty(0).Max();
        Console.WriteLine($"Preparing for {preparingSeconds:0.0} s, then {copy.Count} updates until {total:0.0} s; at each tenth of the time: " +
            string.Join(", ", samples.Select(v => (v * 100).ToString("0"))) + " %" +
            $"; largest gap from even progress {gap * 100:0} points; largest jump {jump * 100:0} points");
        return copy;
    }

    private sealed class SyncProgress(Action<ProgressData> report) : IProgress<ProgressData>
    {
        public void Report(ProgressData value) => report(value);
    }
}
