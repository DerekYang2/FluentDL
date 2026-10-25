using FluentDL.Services.Sources;
using Newtonsoft.Json.Linq;
using QobuzApiSharp.Models.Content;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QobuzArtistCreditTests
{
    // Public track/get credits for Qobuz track 25841966, fetched without a user token.
    internal const string HowDeepCredits = "Calvin Harris, Associated Performer, Composer, Lyricist, Main Artist, Mixing Engineer, Producer\r - Disciples, Associated Performer, Main Artist, Producer\r - Calvin Harris & Disciples, Associated Performer\r - Ina Wrolsden, Composer, Lyricist, Producer\r - Nathan Duvall, Composer, Lyricist\r - Gavin Koolman, Composer, Lyricist\r - Luke Mc Dermott, Composer, Lyricist\r - Mike Marsh, Mastering Engineer";

    private static QobuzSource Source() => new(new TidalParsingTests.DictionarySettings([]));

    [TestMethod]
    public void HowDeepIsYourLoveKeepsBothMainArtistsWithoutAddingProductionCredits()
    {
        var track = Track(HowDeepCredits, "Calvin Harris");
        track.Id = 25841966;
        track.Title = "How Deep Is Your Love";
        CollectionAssert.AreEqual(new[] { "Calvin Harris", "Disciples" }, Source().GetAllContributorsList(HowDeepCredits));
        Assert.AreEqual("Calvin Harris, Disciples", Source().ConvertSongSearchObject(track).Artists);
        Assert.AreEqual("Calvin Harris, Disciples", Source().CreateSongSearchObject(track, track.Album).Artists);
    }

    [TestMethod]
    [DataRow("MainArtist")]
    [DataRow("main-artist")]
    [DataRow("Main Artist")]
    [DataRow("MAIN ARTIST")]
    [DataRow("FeaturedArtist")]
    [DataRow("featured-artist")]
    [DataRow("Featured Artist")]
    [DataRow("Performer")]
    [DataRow("Featuring")]
    public void ArtistRoleSpellingsAreEquivalent(string role)
    {
        CollectionAssert.AreEqual(new[] { "Guest" }, Source().GetAllContributorsList("Guest, " + role));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Calvin Harris, Associated Performer - Someone Else, Composer")]
    public void StructuredTrackPerformerPreventsBlankArtistsWhenCreditRolesAreMissingOrUnknown(string? credits)
    {
        var track = Track(credits, "Calvin Harris");
        Assert.AreEqual("Calvin Harris", Source().ConvertSongSearchObject(track).Artists);
        Assert.AreEqual("Calvin Harris", Source().CreateSongSearchObject(track, track.Album).Artists);
    }

    [TestMethod]
    public void StructuredPerformerLeadsTheListWithoutDuplicatingACaseVariant()
    {
        var track = Track("Other Artist, Featured Artist - primary artist, Main Artist", "Primary Artist");
        Assert.AreEqual("Primary Artist, Other Artist", Source().ConvertSongSearchObject(track).Artists);
    }

    [TestMethod]
    public void CompoundPrimaryNameDoesNotDuplicateAlreadyParsedIndividualArtists()
    {
        var track = Track("Artist A, Main Artist - Artist B, Main Artist", "Artist A & Artist B");
        Assert.AreEqual("Artist A, Artist B", Source().ConvertSongSearchObject(track).Artists);
    }

    [TestMethod]
    public void AlbumArtistIsOnlyAFallbackWhenTrackArtistsAreUnavailable()
    {
        var track = Track(null, null);
        Assert.AreEqual("Album Artist", Source().ConvertSongSearchObject(track).Artists);
        Assert.AreEqual("Supplied Artist", Source().ConvertSongSearchObject(track, "Supplied Artist").Artists);
        track.Performers = "Track Artist, Main Artist";
        Assert.AreEqual("Track Artist", Source().CreateSongSearchObject(track, track.Album).Artists);
    }

    [TestMethod]
    public void ComposerAndEngineeringRolesDoNotBecomeTrackArtists()
    {
        Assert.AreEqual(0, Source().GetAllContributorsList("Composer, Composer, Lyricist - Engineer, Mixing Engineer - Group, Associated Performer").Count);
    }

    private static Track Track(string? credits, string? primary) => new JObject
    {
        ["id"] = 1, ["title"] = "Song", ["performers"] = credits,
        ["performer"] = primary == null ? null : new JObject { ["name"] = primary },
        ["album"] = new JObject
        {
            ["id"] = "album", ["title"] = "Album", ["artist"] = new JObject { ["name"] = "Album Artist" },
            ["image"] = new JObject { ["small"] = "https://covers.example.test/a.jpg" },
        },
    }.ToObject<Track>()!;
}
