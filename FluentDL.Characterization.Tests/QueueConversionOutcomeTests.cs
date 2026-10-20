using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueConversionOutcomeTests
{
    [TestMethod]
    [DataRow(StatusLevel.Success)]
    [DataRow(StatusLevel.Warning)]
    [DataRow(StatusLevel.Error)]
    public void RefreshedTidalMetadataDoesNotChangeTheOperationBadge(StatusLevel level)
    {
        var queued = new SongSearchObject { Source = SourceIds.Tidal, Id = "1" };
        var refreshed = new SongSearchObject { Source = SourceIds.Tidal, Id = "1", TrackPosition = "7" };
        Assert.AreNotEqual(queued, refreshed, "The regression occurs even when source/id match.");
        var summary = new List<(StatusLevel Level, SongSearchObject Track, string? Location)>();
        var outcome = new QueueConversionOutcome((value, track, location) => summary.Add((value, track, location)));

        outcome.Report(level, refreshed, "download.flac");

        Assert.AreEqual(level, outcome.Level);
        Assert.AreEqual((level, refreshed, "download.flac"), summary.Single());
    }

    [TestMethod]
    public void EquivalentTracksAndConcurrentRowsHaveIndependentOutcomes()
    {
        var first = new QueueConversionOutcome((_, _, _) => { });
        var second = new QueueConversionOutcome((_, _, _) => { });
        first.Report(StatusLevel.Success, new() { Source = SourceIds.Deezer, Id = "replacement" });
        second.Report(StatusLevel.Warning, new() { Source = SourceIds.YouTube, Id = "close-match" });

        Assert.AreEqual(StatusLevel.Success, first.Level);
        Assert.AreEqual(StatusLevel.Warning, second.Level);
    }

    [TestMethod]
    public void MissingResultsDefaultToErrorAndLaterFailuresReplaceSuccess()
    {
        var outcome = new QueueConversionOutcome((_, _, _) => { });
        Assert.AreEqual(StatusLevel.Error, outcome.Level);
        outcome.Report(StatusLevel.Success, new() { Source = SourceIds.Tidal, Id = "1" });
        outcome.Report(StatusLevel.Error, new() { Source = SourceIds.Tidal, Id = "1" });
        Assert.AreEqual(StatusLevel.Error, outcome.Level);
    }
}
