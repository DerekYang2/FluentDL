using FluentDL.Models;
using FluentDL.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class CompactResultRowTests
{
    [TestMethod]
    public void TracksHaveNoCountLabel()
    {
        Assert.AreEqual(string.Empty, CompactResultRow.TrackCount(null));
        Assert.AreEqual(string.Empty, CompactResultRow.TrackCount(new SongSearchObject { ReleaseDate = "2024-01-01" }));
    }

    [TestMethod]
    [DataRow(0, "0 tracks")]
    [DataRow(1, "1 track")]
    [DataRow(12, "12 tracks")]
    public void AlbumsShowTheirTrackCount(int count, string expected)
    {
        Assert.AreEqual(expected, CompactResultRow.TrackCount(new AlbumSearchObject { TracksCount = count }));
    }
}
