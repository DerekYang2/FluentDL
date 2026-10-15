using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Sources that were never signed in. Fresh instances, so the shared signed-in ones in TestSources stay as they are.
/// No network.
/// </summary>
[TestClass]
public class SignedOutTests
{
    // Bug 7: a signed-out Qobuz returned null without a callback, so the queue reported nothing for the row.
    [DataTestMethod]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public async Task ConversionReportsNotFound(string target)
    {
        IConversionTarget source = target switch
        {
            "qobuz" => new QobuzSource(TestSources.Settings),
            _ => new SpotifySource(TestSources.Settings, TestSources.SpotifyWebService, TestSources.SpotifyIsrcService),
        };
        var song = new SongSearchObject { Source = "deezer", Id = "67238735", Title = "Get Lucky", Artists = "Daft Punk", AlbumName = "Random Access Memories", Isrc = "USQX91300108" };

        var match = await source.FindEquivalentAsync(song, song.Isrc, CancellationToken.None);

        Assert.AreEqual(MatchKind.NotFound, match.Kind);
    }
}
