using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// The registry and the link routing the Search page does today with its if/else chain of URL prefixes.
/// No network.
/// </summary>
[TestClass]
public class SourceRegistryTests
{
    private static readonly SourceRegistry Registry = new(
        [TestSources.Youtube, TestSources.Spotify, TestSources.Qobuz, TestSources.Deezer]);

    [DataTestMethod]
    [DataRow("https://open.spotify.com/track/69kOkLUCkxIZYexIgSG8rq", SourceIds.Spotify)]
    [DataRow("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", SourceIds.Spotify)]
    [DataRow("https://deezer.page.link/abc", SourceIds.Deezer)]
    [DataRow("https://dzr.page.link/abc", SourceIds.Deezer)]
    [DataRow("https://link.deezer.com/s/abc", SourceIds.Deezer)]
    [DataRow("https://www.deezer.com/track/67238735", SourceIds.Deezer)]
    [DataRow("https://www.deezer.com/en/album/6575789", SourceIds.Deezer)]
    [DataRow("https://www.deezer.com/playlist/1", SourceIds.Deezer)]
    [DataRow("https://www.deezer.com/en/artist/27", null)]
    [DataRow("https://www.qobuz.com/us-en/album/x/y", SourceIds.Qobuz)]
    [DataRow("https://play.qobuz.com/album/y", SourceIds.Qobuz)]
    [DataRow("https://open.qobuz.com/label/4592", SourceIds.Qobuz)]
    [DataRow("https://www.youtube.com/watch?v=5NV6Rdv1a3I", SourceIds.YouTube)]
    [DataRow("https://youtube.com/watch?v=5NV6Rdv1a3I", SourceIds.YouTube)]
    [DataRow("https://music.youtube.com/watch?v=5NV6Rdv1a3I", SourceIds.YouTube)]
    [DataRow("https://youtu.be/5NV6Rdv1a3I", null)]
    [DataRow("daft punk get lucky", null)]
    public void FindLinkHandler_RoutesLikeTheSearchPage(string url, string? expectedSource)
    {
        Assert.AreEqual(expectedSource, Registry.FindLinkHandler(url)?.Descriptor.Id);
    }

    [TestMethod]
    public void Roles_FollowTheCapabilityMatrix()
    {
        string[] Ids<TRole>() where TRole : class, ISource => Registry.All<TRole>().Select(s => s.Descriptor.Id).ToArray();

        // Catalog order, whatever order they were registered in.
        string[] all = [SourceIds.Deezer, SourceIds.Qobuz, SourceIds.Spotify, SourceIds.YouTube];
        CollectionAssert.AreEqual(all, Ids<ISearchSource>());
        CollectionAssert.AreEqual(all, Ids<ILinkSource>());
        CollectionAssert.AreEqual(all, Ids<IConversionTarget>());
        CollectionAssert.AreEqual(all, Ids<ICatalogSource>());
        CollectionAssert.AreEqual(all, Ids<IPreviewSource>());
        CollectionAssert.AreEqual(new[] { SourceIds.Spotify }, Ids<IIsrcSource>());
        CollectionAssert.AreEqual(new[] { SourceIds.Deezer, SourceIds.Qobuz, SourceIds.YouTube }, Ids<IDownloadSource>());
        CollectionAssert.AreEqual(all, Ids<ITagSource>());
        CollectionAssert.AreEqual(new[] { SourceIds.Deezer, SourceIds.Qobuz, SourceIds.Spotify }, Ids<ISourceSession>());

        Assert.IsTrue(Registry.TryGet<ISearchSource>(SourceIds.Qobuz, out var qobuz));
        Assert.IsInstanceOfType<QobuzSource>(qobuz);
        Assert.IsFalse(Registry.TryGet<ISourceSession>(SourceIds.YouTube, out _));
        Assert.IsFalse(Registry.TryGet<ISearchSource>(SourceIds.Local, out _));
    }
}
