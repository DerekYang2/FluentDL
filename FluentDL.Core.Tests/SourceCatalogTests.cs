using System.Text.RegularExpressions;
using FluentDL.Core.Sources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class SourceCatalogTests
{
    // These values were hard-coded across the app before the source registry existed (SourceToColorConverter,
    // Search.xaml.cs chunk sizes, ApiHelper.GetUrl, the queue dialogs). The catalog must keep them.
    [DataTestMethod]
    [DataRow("deezer", "Deezer", 160, 55, 250, true, 20, 10, "https://www.deezer.com/track/x", "https://www.deezer.com/album/x")]
    [DataRow("qobuz", "Qobuz", 0, 0, 0, true, 20, 10, "https://open.qobuz.com/track/x", "https://open.qobuz.com/album/x")]
    [DataRow("spotify", "Spotify", 15, 213, 101, true, 20, 10, "https://open.spotify.com/track/x", "https://open.spotify.com/album/x")]
    [DataRow("youtube", "YouTube", 255, 0, 0, true, 10, 5, "https://www.youtube.com/watch?v=x", "https://music.youtube.com/playlist?list=x")]
    [DataRow("local", "Local", 255, 255, 255, false, 0, 0, "x", "x")]
    public void Catalog_KeepsThePreviousValues(string id, string name, int r, int g, int b, bool searchable, int initial, int chunk, string trackUrl, string albumUrl)
    {
        var source = SourceCatalog.Find(id);

        Assert.IsNotNull(source);
        Assert.AreEqual(name, source.DisplayName);
        Assert.AreEqual((byte)r, source.R);
        Assert.AreEqual((byte)g, source.G);
        Assert.AreEqual((byte)b, source.B);
        Assert.AreEqual(searchable, source.IsSearchable);
        Assert.AreEqual(initial, source.InitialChunkSize);
        Assert.AreEqual(chunk, source.SearchChunkSize);
        Assert.AreEqual(trackUrl, source.TrackUrl("x"));
        Assert.AreEqual(albumUrl, source.AlbumUrl("x"));
    }

    [TestMethod]
    public void Catalog_KeepsTheUiOrder()
    {
        CollectionAssert.AreEqual(new[] { "deezer", "qobuz", "spotify", "youtube", "local" }, SourceCatalog.All.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "deezer", "qobuz", "spotify", "youtube" }, SourceCatalog.Searchable.Select(s => s.Id).ToArray());
    }

    [TestMethod]
    public void Find_ReturnsNullForUnknownIds()
    {
        Assert.IsNull(SourceCatalog.Find("tidal"));
        Assert.IsNull(SourceCatalog.Find(null));
        Assert.IsNull(SourceCatalog.Find("Deezer"));
    }

    // Source ids are persisted, so every use outside SourceIds should go through the constants. A quoted lowercase
    // id elsewhere usually means a new hard-coded per-source branch.
    [TestMethod]
    public void AppCode_UsesSourceIdConstants()
    {
        var root = FindRepoRoot();
        var allowed = new[] { Path.Combine("FluentDL.Core", "Sources", "SourceIds.cs") };
        var literal = new Regex("\"(deezer|qobuz|spotify|youtube|local)\"");

        var offenders = new[] { "FluentDL", "FluentDL.Core" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs") || file.EndsWith(".xaml"))
            .Where(file => !Regex.IsMatch(file, @"[\\/](bin|obj|PackageSelect)[\\/]"))
            .Where(file => !allowed.Any(a => file.EndsWith(a)))
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(x => literal.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        Assert.AreEqual(0, offenders.Count, "Use SourceIds instead of quoted source ids:\n" + string.Join("\n", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "FluentDL.Core")) && Directory.Exists(Path.Combine(dir.FullName, "FluentDL"))))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
