using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueAlbumDownloadTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnreadableOutputKeepsEverySourceTrackInItsOriginalPosition(bool throws)
    {
        var first = Track("1");
        var second = Track("2");
        var third = Track("3");
        var local = new SongSearchObject { Source = SourceIds.Local, Id = "saved.flac", Title = "Duplicate title" };
        var outcomes = new[]
        {
            new AlbumTrackDownloadOutcome(third, 2, StatusLevel.Error, null, "Metadata failed"),
            new AlbumTrackDownloadOutcome(first, 0, StatusLevel.Success, "unreadable.flac", null),
            new AlbumTrackDownloadOutcome(second, 1, StatusLevel.Warning, "saved.flac", null),
        };
        var rows = QueueAlbumDownload.Resolve(outcomes, path => path == "saved.flac" ? local
            : throws ? throw new IOException("Synthetic unreadable output") : null);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(row => row.Order).ToArray());
        Assert.AreSame(first, rows[0].Song);
        Assert.AreSame(local, rows[1].Song);
        Assert.AreSame(third, rows[2].Song);
        CollectionAssert.AreEqual(new[] { StatusLevel.Error, StatusLevel.Warning, StatusLevel.Error }, rows.Select(row => row.Level).ToArray());
    }

    private static SongSearchObject Track(string id) => new() { Source = SourceIds.Tidal, Id = id, Title = "Duplicate title" };
}
