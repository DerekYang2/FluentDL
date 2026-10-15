using FluentDL.Core.Matching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class AlbumMatchTests
{
    [TestMethod]
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories", "Daft Punk", 13, AlbumMatchLevel.Exact)]
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories", "Daft Punk", 0, AlbumMatchLevel.Exact)] // unknown count
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories (10th Anniversary Edition)", "Daft Punk", 22, AlbumMatchLevel.Close)]
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories", "Daft Punk", 22, AlbumMatchLevel.Close)]
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories", "Some Tribute Band", 13, AlbumMatchLevel.None)]
    [DataRow("Alive 2007", "Daft Punk", 12, "Homework", "Daft Punk", 16, AlbumMatchLevel.None)]
    [DataRow("Discovery", "Daft Punk", 14, "Discovery (Live)", "Daft Punk", 14, AlbumMatchLevel.None)] // "Live" must agree
    [DataRow("Get Lucky (feat. Pharrell Williams)", "Daft Punk, Pharrell Williams", 1, "Get Lucky", "Pharrell Williams", 1, AlbumMatchLevel.Exact)]
    [DataRow("Random Access Memories", "Daft Punk", 13, "Random Access Memories (Remastered)", "Daft Punk", 13, AlbumMatchLevel.Close)]
    [DataRow("THE BOOK", "YOASOBI", 10, "THE BOOK (Deluxe Edition)", "YOASOBI", 12, AlbumMatchLevel.Close)]
    [DataRow("夜に駆ける", "YOASOBI", 1, "夜に駆ける", "YOASOBI", 1, AlbumMatchLevel.Exact)]
    [DataRow("夜に駆ける", "YOASOBI", 1, "群青", "YOASOBI", 1, AlbumMatchLevel.None)]
    public void ComparesTitleArtistAndTrackCount(string title, string artists, int tracks, string other, string otherArtists, int otherTracks, AlbumMatchLevel expected) =>
        Assert.AreEqual(expected, AlbumMatch.Compare(title, artists, tracks, other, otherArtists, otherTracks));
}
