using System.Globalization;
using System.Net;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Models;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Services;
using FluentDL.Services.Sources;
using Newtonsoft.Json.Linq;
using YoutubeExplode;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models;
using YouTubeMusicAPI.Models.Search;

namespace FluentDL.Characterization.Tests;

[TestClass]
[DoNotParallelize]
public class YoutubeSearchResultTests
{
    [TestMethod]
    [TestCategory("Live")]
    [TestCategory("YouTube")]
    public async Task LightweightSearchRowLoadsViewsAndUploadDateOnlyForThePreview()
    {
        var rows = new List<SongSearchObject>();
        await TestSources.Youtube.SearchAsync(rows, "daft punk get lucky", new(0, 1), false, CancellationToken.None);
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("", rows[0].ReleaseDate);
        Assert.AreEqual("", rows[0].Rank);

        var preview = await TestSources.Youtube.GetPreviewAsync(rows[0]);

        Assert.IsFalse(string.IsNullOrEmpty(preview.ReleaseDate));
        Assert.IsFalse(string.IsNullOrEmpty(preview.Rows.Single(r => r.Label == "Views").Value));
        Assert.IsFalse(string.IsNullOrEmpty(preview.CoverUrl));
    }

    [TestMethod]
    [DataRow(0, 3)]
    [DataRow(1, 1)]
    public async Task MusicSearchUsesOneRequestAndNoPerResultLookups(int offset, int limit)
    {
        var handler = new SearchHandler();
        var source = Source(handler);
        var rows = new List<SongSearchObject>();

        await source.SearchAsync(rows, "test", new(offset, limit), false, CancellationToken.None);

        Assert.AreEqual(limit, rows.Count);
        Assert.AreEqual("Song " + offset, rows[0].Title);
        Assert.AreEqual("Artist", rows[0].Artists);
        Assert.AreEqual("Album", rows[0].AlbumName);
        Assert.AreEqual("369", rows[0].Duration);
        Assert.AreEqual("", rows[0].ReleaseDate);
        Assert.AreEqual("", rows[0].Rank);
        CollectionAssert.AreEqual(new[] { "music.youtube.com/youtubei/v1/search" }, handler.Requests);
    }

    [TestMethod]
    public void MappingPreservesMusicFieldsAndArtistsWithoutChannelIds()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            var song = new SongSearchResult("Title", "4D7u5KF7SP8", [new("Artist", "UCArtist"), new("Guest", null)],
                new NamedEntity("Album", "MPREAlbum"), TimeSpan.FromSeconds(123.5), true, "1M plays", null,
                [new("https://example.com/large.jpg", 320, 320), new("https://example.com/small.jpg", 120, 120)]);

            var row = YoutubeSource.ConvertSongSearchResult(song);

            Assert.AreEqual("youtube", row.Source);
            Assert.AreEqual(song.Id, row.Id);
            Assert.AreEqual("Artist, Guest", row.Artists);
            Assert.AreEqual("Album", row.AlbumName);
            Assert.AreEqual("123.5", row.Duration);
            Assert.IsTrue(row.Explicit);
            Assert.AreEqual("https://example.com/large.jpg", row.ImageLocation);
            Assert.IsNull(row.Isrc);
            Assert.AreEqual("", row.Rank, "Music plays are not video views.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void MissingAlbumAndThumbnailDoNotFailTheRow()
    {
        var song = new SongSearchResult("Title", "4D7u5KF7SP8", [new("Artist", null)], null,
            TimeSpan.Zero, false, "", null, []);
        var row = YoutubeSource.ConvertSongSearchResult(song);
        Assert.AreEqual("", row.AlbumName);
        Assert.AreEqual("ms-appx:///Assets/Unloaded.jpg", row.ImageLocation);
        Assert.AreEqual("Artist", row.Artists);
    }

    [TestMethod]
    public async Task CancelledMusicSearchDoesNotStartVideoFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new SearchHandler { OnRequest = cancellation.Cancel };
        var source = Source(handler);
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            source.SearchAsync(new List<SongSearchObject>(), "test", new(0, 3), false, cancellation.Token));
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.StartsWith(handler.Requests[0], "music.youtube.com/");
    }

    [TestMethod]
    public async Task DownloadResolvesTheRowBeforeYearFolderAndFilenameTemplates()
    {
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            ["subfolders"] = true, ["subfolder_wildcard"] = "{artist}/{year}",
            ["file_naming_wildcard"] = "{year} - {title}", ["download_lyrics"] = false,
        });
        var video = new FullVideoSource();
        var registry = new SourceRegistry([video]);
        var downloads = new DownloadService(registry, new EquivalentDownloader(registry), new NoLyrics());
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "youtube-naming-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        SourceServices.Configure(settings, TestSources.AudioConverter);
        try
        {
            var row = new SongSearchObject { Source = "youtube", Id = "4D7u5KF7SP8", Title = "Title", Artists = "Artist" };
            var path = await downloads.DownloadObject(row, folder, new Progress<ProgressData>());
            Assert.AreEqual(Path.Combine(folder, "Artist", "2020", "2020 - Title.mp3"), path);
            Assert.AreEqual(1, video.Lookups);
            Assert.AreEqual("2020-01-02", video.Downloaded!.ReleaseDate);
        }
        finally
        {
            SourceServices.Configure(TestSources.Settings, TestSources.AudioConverter);
            Directory.Delete(folder, true);
        }
    }

    private sealed class NoLyrics : ILyricService
    {
        public Task<string?> GetLyricsAsync(SongSearchObject song) => Task.FromResult<string?>(null);
    }

    private sealed class FullVideoSource : ICatalogSource, IDownloadSource
    {
        public SourceDescriptor Descriptor => SourceCatalog.Find(SourceIds.YouTube)!;
        public int Lookups;
        public SongSearchObject? Downloaded;
        public Task<SongSearchObject?> GetTrackAsync(string id)
        {
            Lookups++;
            return Task.FromResult<SongSearchObject?>(new SongSearchObject
            {
                Source = "youtube", Id = id, Title = "Title", Artists = "Artist", ReleaseDate = "2020-01-02",
            });
        }
        public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => throw new NotSupportedException();
        public Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) => throw new NotSupportedException();
        public async Task<string> DownloadAsync(SongSearchObject track, string path, DownloadRequest request, IProgress<ProgressData>? progress)
        {
            Downloaded = track;
            await File.WriteAllBytesAsync(path + ".mp3", [1, 2, 3]);
            return path + ".mp3";
        }
    }

    private static YoutubeSource Source(SearchHandler handler)
    {
        var http = new HttpClient(handler);
        return new(new TidalParsingTests.DictionarySettings([]), TestSources.AudioConverter,
            new YouTubeMusicClient(httpClient: http), new YoutubeClient(http));
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public Action? OnRequest { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.Host + request.RequestUri.AbsolutePath);
            OnRequest?.Invoke();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            if (request.RequestUri.Host != "music.youtube.com" || request.RequestUri.AbsolutePath != "/youtubei/v1/search")
                throw new InvalidOperationException("Search must not fetch video or song details.");
            var item = JObject.Parse("""
                {"musicResponsiveListItemRenderer":{
                  "flexColumns":[
                    {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Song"}]}}},
                    {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[
                      {"text":"Artist","navigationEndpoint":{"browseEndpoint":{"browseId":"UCArtist"}}},
                      {"text":" • "},
                      {"text":"Album","navigationEndpoint":{"browseEndpoint":{"browseId":"MPREAlbum"}}},
                      {"text":" • "},{"text":"6:09"}
                    ]}}},
                    {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"1M plays"}]}}}
                  ],
                  "overlay":{"musicItemThumbnailOverlayRenderer":{"content":{"musicPlayButtonRenderer":{
                    "playNavigationEndpoint":{"watchEndpoint":{"videoId":"4D7u5KF7SP8"}}
                  }}}},
                  "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[
                    {"url":"https://example.com/cover.jpg","width":120,"height":120}
                  ]}}}
                }}
                """);
            var items = new JArray();
            foreach (var index in Enumerable.Range(0, 3))
            {
                var copy = item.DeepClone();
                copy.SelectToken("musicResponsiveListItemRenderer.flexColumns[0].musicResponsiveListItemFlexColumnRenderer.text.runs[0].text")!.Replace("Song " + index);
                items.Add(copy);
            }
            var json = JObject.Parse("""
                {"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{
                  "sectionListRenderer":{"contents":[{"musicShelfRenderer":{"title":{"runs":[{"text":"Songs"}]},"contents":[]}}]}
                }}}]}}}
                """);
            json.SelectToken("contents.tabbedSearchResultsRenderer.tabs[0].tabRenderer.content.sectionListRenderer.contents[0].musicShelfRenderer.contents")!.Replace(items);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToString()) });
        }
    }
}
