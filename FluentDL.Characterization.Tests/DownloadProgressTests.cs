using System.Collections.Concurrent;
using System.Globalization;
using FluentDL.Core.Sources;
using FluentDL.Helpers;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class DownloadProgressTests
{
    [TestMethod]
    public void LabelAndBarUseTheSamePercentage()
    {
        var helper = new DownloadProgressHelper("TIDAL");
        helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 409, fraction = 0.409 });
        AssertProgress(helper, 0.409);
        helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 990, totalBytes = 1000 });
        AssertProgress(helper, 0.99);
        helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 1000, totalBytes = 1000 });
        AssertProgress(helper, 1);
    }

    // YouTube: a long link lookup, then the whole stream in about a second, then FFmpeg.
    [TestMethod]
    public void FastTransferShowsEveryStageAndConversionIsNotComplete()
    {
        var helper = new DownloadProgressHelper("YouTube");
        var track = new TrackDownloadProgress(new InlineProgress(helper.UpdateProgressLoop));
        var preparing = helper.GetProgressUpdate()!;
        Assert.IsTrue(preparing.Preparing);
        Assert.AreEqual(0, preparing.ProgressValue);
        StringAssert.Contains(preparing.DisplayString, "Preparing");
        Assert.IsFalse(preparing.DisplayString.Contains('%'));
        track.Report(new ProgressData { uniqueId = 99, currentBytes = 500, totalBytes = 1000 });
        AssertProgress(helper, 0.475);
        track.Report(new ProgressData { uniqueId = 99, currentBytes = 1000, totalBytes = 1000 });
        AssertProgress(helper, 0.95);
        track.Finish();
        AssertProgress(helper, 1);
    }

    // Deezer: no report until the whole file has arrived. After the first track, estimate the others by time.
    [TestMethod]
    public async Task TracksWithoutLiveProgressAreEstimatedFromFinishedTracks()
    {
        var helper = new DownloadProgressHelper("Deezer album", 4);
        var progress = new InlineProgress(helper.UpdateProgressLoop);
        var first = new TrackDownloadProgress(progress);
        await Task.Delay(200);
        Assert.IsTrue(helper.GetProgressUpdate()!.Preparing, "No data yet: show an indeterminate bar, not 0%.");
        first.Report(new ProgressData { currentBytes = 100, totalBytes = 100 });
        first.Finish();
        var second = new TrackDownloadProgress(progress);
        var afterFirst = helper.GetProgressUpdate()!.ProgressValue;
        Assert.IsTrue(afterFirst >= 0.25, "The finished track has its full share.");
        await Task.Delay(100);
        var estimated = helper.GetProgressUpdate()!.ProgressValue;
        Assert.IsTrue(estimated > afterFirst, "The track still downloading moves forward.");
        Assert.IsTrue(estimated <= 0.25 + 0.25 * 0.9 + 1e-9, "An estimate never completes a track.");
        await Task.Delay(400);
        Assert.IsTrue(helper.GetProgressUpdate()!.ProgressValue <= 0.25 + 0.25 * 0.9 + 1e-9);
        second.Report(new ProgressData { currentBytes = 100, totalBytes = 100 });
        second.Finish();
        AssertProgress(helper, 0.5);
        StringAssert.Contains(helper.GetProgressUpdate()!.DisplayString, "(2 / 4)");
    }

    [TestMethod]
    public void AlbumWithDifferentSizesAndSourcesGivesEachTrackAnEqualShare()
    {
        var helper = new DownloadProgressHelper("Album", 4);
        helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 100, totalBytes = 100 });
        helper.UpdateProgressLoop(new ProgressData { uniqueId = 2, currentBytes = 50000, totalBytes = 100000 });
        AssertProgress(helper, 1.5 / 4);
        StringAssert.Contains(helper.GetProgressUpdate()!.DisplayString, "(1 / 4)");
    }

    [TestMethod]
    public void FailedTrackCountsAsProcessedAndLateReportsAreIgnored()
    {
        var helper = new DownloadProgressHelper("Album", 2);
        var reports = new List<ProgressData>();
        var track = new TrackDownloadProgress(new InlineProgress(value => { reports.Add(value); helper.UpdateProgressLoop(value); }));
        track.Report(new ProgressData { uniqueId = 10, currentBytes = 20, totalBytes = 100 });
        track.Report(new ProgressData { uniqueId = 11, currentBytes = 10, totalBytes = 100 });
        AssertProgress(helper, 0.5 * 0.2 * 0.95);
        track.Finish();
        track.Report(new ProgressData { uniqueId = 11, currentBytes = 95, totalBytes = 100 });
        AssertProgress(helper, 0.5);
        Assert.AreEqual(1, reports.Select(r => r.uniqueId).Distinct().Count(), "A retry is the same track.");
    }

    [TestMethod]
    public void ConcurrentTransfersHaveDifferentProgressIds()
    {
        var ids = new ConcurrentBag<long>();
        Parallel.For(0, 1000, _ => ids.Add(ProgressData.NewId()));
        Assert.AreEqual(1000, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task LoopKeepsPublishingUntilStoppedSoCompletionIsShown()
    {
        var helper = new DownloadProgressHelper("Download");
        var complete = new TaskCompletionSource<ProgressEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        helper.ProgressUpdated += (_, update) => { if (update.ProgressValue >= 1) complete.TrySetResult(update); };
        helper.StartLoop();
        try
        {
            // The whole download happens between two loop ticks.
            helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 10, totalBytes = 100 });
            helper.UpdateProgressLoop(new ProgressData { uniqueId = 1, currentBytes = 100, totalBytes = 100 });
            AssertUpdate(await complete.Task.WaitAsync(TimeSpan.FromSeconds(5)), 1);
        }
        finally { helper.StopLoop(); }
    }

    [TestMethod]
    public void EasingGlidesUpButNeverPassesTheMeasuredProgress()
    {
        double shown = 0;
        var steps = new List<double>();
        for (var i = 0; i < 20; i++) steps.Add(shown = DownloadProgressHelper.Ease(shown, 0.6, 0.1));
        Assert.IsTrue(steps[0] > 0 && steps[0] < 0.3, "A jump is spread over several frames.");
        Assert.IsTrue(steps.All(v => v <= 0.6), "Never ahead of the measured progress.");
        Assert.IsTrue(steps.Zip(steps.Skip(1)).All(p => p.Second >= p.First), "Never backwards.");
        Assert.AreEqual(0.6, steps.Last(), "Reaches the measured value, not just near it.");
        Assert.AreEqual(0.6, DownloadProgressHelper.Ease(0.6, 0.5, 0.1), "A lower measurement never pulls it back.");
        Assert.AreEqual(1, DownloadProgressHelper.Ease(0.9995, 1, 0.1));
    }

    private sealed class InlineProgress(Action<ProgressData> report) : IProgress<ProgressData>
    {
        public void Report(ProgressData value) => report(value);
    }

    private static void AssertProgress(DownloadProgressHelper helper, double expected) =>
        AssertUpdate(helper.GetProgressUpdate(5)!, expected);

    private static void AssertUpdate(ProgressEventArgs update, double expected)
    {
        Assert.IsNotNull(update);
        Assert.AreEqual(expected, update.ProgressValue, 0.000001);
        var text = update.DisplayString.Split(" • ")[1].Split('%')[0];
        var displayed = double.Parse(text, CultureInfo.CurrentCulture) / 100;
        Assert.AreEqual(update.ProgressValue, displayed, 0.00051, "The text rounds to one decimal but matches the bar.");
    }
}
