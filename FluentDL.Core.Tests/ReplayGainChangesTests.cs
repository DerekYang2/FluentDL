using FluentDL.Core.ReplayGain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class ReplayGainChangesTests
{
    // A track measured at -3.00 dB with peak 0.75, on an album at -1.50 dB with peak 0.9.
    private static ReplayGainResult Result(ReplayGainTagValues? existing, double? albumGain = -1.5, double? albumPeak = 0.9) =>
        new("song.flac", -15.0, -3.0, 0.75, false, false, albumGain, albumPeak, false, false, existing);

    [TestMethod]
    public void Changes_AddsEveryRequestedValue_WhenTheFileHasNoTags()
    {
        // Arrange
        var result = Result(existing: null);

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions());

        // Assert
        CollectionAssert.AreEqual(
            new[] { ReplayGainValue.TrackGain, ReplayGainValue.TrackPeak, ReplayGainValue.AlbumGain, ReplayGainValue.AlbumPeak },
            changes.Select(change => change.Value).ToArray());
        Assert.IsTrue(changes.All(change => change.Current is null));
    }

    [TestMethod]
    public void Changes_IsEmpty_WhenTheTagsMatchAsTagText()
    {
        // Arrange: tags store two decimals for gains and six for peaks, so these round to the measured values.
        var result = Result(new ReplayGainTagValues(-3.004, 0.7500004, -1.5, 0.9));

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions());

        // Assert
        Assert.AreEqual(0, changes.Count);
        Assert.IsTrue(ReplayGainScanner.HasTags(result, new ReplayGainOptions()));
    }

    [TestMethod]
    public void Changes_ListsOnlyTheValuesThatDiffer()
    {
        // Arrange
        var result = Result(new ReplayGainTagValues(-2.5, 0.75, -1.5, 0.9));

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions());

        // Assert
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(new ReplayGainChange(ReplayGainValue.TrackGain, -2.5, -3.0), changes[0]);
    }

    [TestMethod]
    public void Changes_FindsAPeakThatDiffers_WhenTheGainsMatch()
    {
        // Arrange
        var result = Result(new ReplayGainTagValues(-3.0, 0.5, -1.5, 0.9));

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions());

        // Assert
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(new ReplayGainChange(ReplayGainValue.TrackPeak, 0.5, 0.75), changes[0]);
        Assert.IsFalse(ReplayGainScanner.HasTags(result, new ReplayGainOptions()));
    }

    [TestMethod]
    public void Changes_LeavesOutAlbumValues_WhenTheAlbumHasNoGain()
    {
        // Arrange: a track on the album couldn't be measured, so there's no album gain to write.
        var result = Result(new ReplayGainTagValues(-3.0, 0.75, null, null), albumGain: null, albumPeak: null);

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions());

        // Assert
        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void Changes_LeavesOutValuesTheOptionsDontWrite()
    {
        // Arrange
        var result = Result(new ReplayGainTagValues(null, null, -1.5, 0.9));

        // Act
        var changes = ReplayGainScanner.Changes(result, new ReplayGainOptions(TrackGain: false));

        // Assert
        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void HasTags_IsTrue_WhenThereIsNothingToWrite()
    {
        // Arrange: only album gain is wanted, the album has none, and the file's tags couldn't be read.
        var result = Result(existing: null, albumGain: null, albumPeak: null);

        // Act
        var hasTags = ReplayGainScanner.HasTags(result, new ReplayGainOptions(TrackGain: false));

        // Assert
        Assert.IsTrue(hasTags);
    }
}
