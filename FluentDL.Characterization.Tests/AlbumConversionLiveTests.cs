using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Characterization.Tests;

/// <summary>Converts the fixture album between real services, the way the queue converts a queued album.</summary>
[TestClass]
[TestCategory("Live")]
public class AlbumConversionLiveTests
{
    [TestMethod]
    [DataRow(SourceIds.Tidal, "UPC")]
    [DataRow(SourceIds.Qobuz, "album search")]
    public async Task DeezerAlbumConvertsToTheSameRelease(string target, string route)
    {
        await LiveSources.EnsureDeezerAsync();
        switch (target)
        {
            case SourceIds.Tidal: await LiveSources.EnsureTidalAsync(); break;
            case SourceIds.Qobuz: await LiveSources.EnsureQobuzAsync(); break;
        }
        var items = new List<SongSearchObject>();
        await TestSources.Deezer.AddFromLinkAsync(items, $"https://www.deezer.com/album/{Fixtures.DeezerAlbumId}",
            new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
        var album = (AlbumSearchObject)items.Single();
        var match = await TestSources.Conversions.FindEquivalentAsync(album, target, CancellationToken.None);
        Console.WriteLine($"{album.Title} ({album.TracksCount} tracks, UPC {album.Upc ?? "none"}) -> {target} via {route}: " +
            $"{match.Kind} {match.Track?.Title} ({(match.Track as AlbumSearchObject)?.TracksCount} tracks)");
        Assert.AreNotEqual(MatchKind.NotFound, match.Kind);
        var found = match.Track as AlbumSearchObject;
        Assert.IsNotNull(found, "An album converts to an album.");
        Assert.AreEqual(target, found.Source);
        StringAssert.Contains(found.Title, "Random Access Memories");
        StringAssert.Contains(found.Artists, "Daft Punk");
    }

    [TestMethod]
    public async Task QobuzAlbumConvertsToDeezerByUpc()
    {
        await LiveSources.EnsureQobuzAsync();
        await LiveSources.EnsureDeezerAsync();
        var items = new List<SongSearchObject>();
        await TestSources.Qobuz.AddFromLinkAsync(items, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}",
            new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
        var album = (AlbumSearchObject)items.Single();
        Assert.IsFalse(string.IsNullOrWhiteSpace(album.Upc), "The Qobuz album should carry its UPC.");
        var match = await TestSources.Conversions.FindEquivalentAsync(album, SourceIds.Deezer, CancellationToken.None);
        Console.WriteLine($"{album.Title} UPC {album.Upc} -> Deezer: {match.Kind} {match.Track?.Title} {match.Track?.Id}");
        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(Fixtures.DeezerAlbumId, match.Track!.Id);
    }
}
