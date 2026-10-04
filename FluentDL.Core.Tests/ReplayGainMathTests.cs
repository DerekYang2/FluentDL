using FluentDL.Core.ReplayGain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class ReplayGainMathTests
{
    [DataTestMethod]
    [DataRow(-23.0, 0.0, 5.0)]
    [DataRow(-12.3, 0.0, -5.7)]
    [DataRow(-12.3, -5.0, -10.7)]
    [DataRow(-18.0, 2.5, 2.5)]
    public void Gain_NormalizesToMinus18Lufs_PlusTheOffset(double loudness, double offset, double expected)
    {
        // Act
        var gain = ReplayGainMath.Gain(loudness, offset);

        // Assert
        Assert.AreEqual(expected, gain, 1e-9);
    }

    [TestMethod]
    public void Gain_IsZeroForSilentTracks()
    {
        // Act
        var gain = ReplayGainMath.Gain(double.NegativeInfinity, -5);

        // Assert
        Assert.AreEqual(0.0, gain);
    }

    [DataTestMethod]
    [DataRow(6.0, 0.5, false)]
    [DataRow(6.1, 0.5, true)]
    [DataRow(0.0, 1.0, false)]
    [DataRow(-3.0, 1.2, false)]
    [DataRow(-1.0, 1.2, true)]
    public void WouldClip_ComparesTheAmplifiedPeakWithFullScale(double gain, double peak, bool expected)
    {
        // Act
        var clips = ReplayGainMath.WouldClip(gain, peak);

        // Assert
        Assert.AreEqual(expected, clips);
    }

    [TestMethod]
    public void PreventClipping_LowersPositiveGainsToFullScale()
    {
        // Arrange: a peak of 0.5 allows at most +6.0206 dB, which tags store as +6.02 dB.
        var gain = 10.0;

        // Act
        var lowered = ReplayGainMath.PreventClipping(gain, 0.5);

        // Assert: rounded down, so the stored value doesn't clip either.
        Assert.AreEqual(6.02, lowered, 1e-9);
        Assert.IsFalse(ReplayGainMath.WouldClip(lowered, 0.5));
    }

    [DataTestMethod]
    [DataRow(3.0, 1.5, 0.0)]
    [DataRow(-2.0, 1.5, -2.0)]
    [DataRow(2.0, 0.5, 2.0)]
    public void PreventClipping_NeverGoesBelowZeroAndLeavesOtherGainsAlone(double gain, double peak, double expected)
    {
        // Act
        var lowered = ReplayGainMath.PreventClipping(gain, peak);

        // Assert
        Assert.AreEqual(expected, lowered, 1e-9);
    }

    [TestMethod]
    public void GroupByAlbum_UsesAlbumArtistAndAlbum_WithTheFolderAsFallback()
    {
        // Arrange
        var tracks = new[]
        {
            new ReplayGainTrack(@"C:\Music\A\01.flac", "Album", "Artist"),
            new ReplayGainTrack(@"C:\Music\B\02.flac", "album", "artist"),
            new ReplayGainTrack(@"C:\Music\C\01.flac", "Album", "Someone Else"),
            new ReplayGainTrack(@"C:\Music\D\01.flac", "Greatest Hits", null),
            new ReplayGainTrack(@"C:\Music\E\01.flac", "Greatest Hits", null),
            new ReplayGainTrack(@"C:\Music\F\01.flac", null, null),
            new ReplayGainTrack(@"C:\Music\F\02.flac", "", "Artist"),
        };

        // Act
        var albums = ReplayGainMath.GroupByAlbum(tracks, track => track, singleAlbum: false);

        // Assert
        var sizes = albums.Select(album => album.Count).OrderByDescending(count => count).ToArray();
        CollectionAssert.AreEqual(new[] { 2, 2, 1, 1, 1 }, sizes);
        Assert.IsTrue(albums.Any(album => album.Count == 2 && album.All(track => track.Album?.Equals("album", StringComparison.OrdinalIgnoreCase) == true)));
        Assert.IsTrue(albums.Any(album => album.Count == 2 && album.All(track => track.Path.StartsWith(@"C:\Music\F\"))));
    }

    [TestMethod]
    public void GroupByAlbum_GivesATrackWithoutAnAlbumArtistItsFoldersAlbumArtist()
    {
        // Arrange: one track of an album is missing its album artist; another folder has a different artist's
        // album of the same name, and a third folder disagrees about the artist, so nothing is guessed there.
        var tracks = new[]
        {
            new ReplayGainTrack(@"C:\Music\A\01.flac", "Album", "Artist"),
            new ReplayGainTrack(@"C:\Music\A\02.flac", "Album", null),
            new ReplayGainTrack(@"C:\Music\B\01.flac", "Album", "Other"),
            new ReplayGainTrack(@"C:\Music\C\01.flac", "Album", "One"),
            new ReplayGainTrack(@"C:\Music\C\02.flac", "Album", "Two"),
            new ReplayGainTrack(@"C:\Music\C\03.flac", "Album", null),
        };

        // Act
        var albums = ReplayGainMath.GroupByAlbum(tracks, track => track, singleAlbum: false);

        // Assert
        var folderA = albums.Single(album => album.Any(track => track.Path == @"C:\Music\A\02.flac"));
        CollectionAssert.AreEquivalent(new[] { @"C:\Music\A\01.flac", @"C:\Music\A\02.flac" }, folderA.Select(track => track.Path).ToList());
        Assert.AreEqual(1, albums.Single(album => album.Any(track => track.Path == @"C:\Music\C\03.flac")).Count);
    }

    [TestMethod]
    public void GroupByAlbum_CanTreatEverythingAsOneAlbum()
    {
        // Arrange
        var tracks = new[] { new ReplayGainTrack(@"C:\a.flac", "One"), new ReplayGainTrack(@"C:\b.flac", "Two") };

        // Act
        var albums = ReplayGainMath.GroupByAlbum(tracks, track => track, singleAlbum: true);

        // Assert
        Assert.AreEqual(1, albums.Count);
        Assert.AreEqual(2, albums[0].Count);
    }
}
