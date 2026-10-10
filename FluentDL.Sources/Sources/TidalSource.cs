using System.Text.Json;
using System.Xml;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services.Tidal;
using static FluentDL.Services.Tidal.JsonApiDocument;

namespace FluentDL.Services.Sources;

/// <summary>
/// TIDAL through its official API. It only reads the catalog: search, lookups and links, with audio from other
/// sources (see ADR-003). Signs in with a client id and secret for now; a TIDAL account login (PKCE) comes later.
/// </summary>
internal sealed class TidalSource : ISourceSession, ISearchSource
{
    private const string TrackIncludes = "tracks,tracks.artists,tracks.albums.coverArt";
    private const string AlbumIncludes = "albums,albums.artists,albums.coverArt";

    private readonly TidalApi api;
    private readonly SemaphoreSlim searchLock = new(1, 1);
    private SearchCursor? lastSearch;

    public TidalSource(TidalApi api)
    {
        this.api = api;
    }

    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Tidal)!;

    public bool IsConnected => api.IsSignedIn;

    public string? StatusText => IsConnected ? $"Signed in with a client ID and secret\nCountry: {api.CountryCode}" : null;

    public async Task<ConnectResult> ConnectAsync()
    {
        try
        {
            await api.SignInAsync();
            return new ConnectResult(true, StatusLevel.Success, "Signed in to TIDAL");
        }
        catch (InvalidOperationException e) // no client id or secret saved
        {
            return new ConnectResult(false, StatusLevel.Warning, e.Message);
        }
        catch (Exception e)
        {
            return new ConnectResult(false, StatusLevel.Error, $"Couldn't sign in to TIDAL: {e.Message}");
        }
    }

    public async Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return;
        }
        foreach (var item in await SearchRangeAsync(query, albumMode, page.Offset, page.Limit, token))
        {
            results.Add(item);
        }
    }

    // TIDAL has no field search, so the fields are searched as plain words and the results filtered by artist, as for
    // Deezer. A page can hold fewer tracks than its limit.
    public async Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token)
    {
        var words = string.Join(" ", new[] { query.Artist, query.Track, query.Album }.Select(w => w.Trim()).Where(w => w.Length > 0));
        if (words.Length == 0)
        {
            return;
        }
        foreach (var item in await SearchRangeAsync(words, albumMode: false, page.Offset, page.Limit, token))
        {
            if (query.Artist.Trim().Length == 0 || FuzzyMatch.AnyArtistMatches(query.Artist.Trim(), item.Artists))
            {
                results.Add(item);
            }
        }
    }

    // The search the Search page is paging through. TIDAL pages by cursor, 20 results a page; the page asks by offset
    // and limit, in order. Offset 0 starts a new search; later offsets fetch pages forward until they are covered.
    private sealed class SearchCursor(string key)
    {
        public string Key { get; } = key;
        public List<SongSearchObject> Items { get; } = [];
        public string? Next { get; set; }
    }

    private async Task<IReadOnlyList<SongSearchObject>> SearchRangeAsync(string query, bool albumMode, int offset, int limit, CancellationToken token)
    {
        var kind = albumMode ? "albums" : "tracks";
        var include = albumMode ? AlbumIncludes : TrackIncludes;
        await searchLock.WaitAsync(token);
        try
        {
            var key = kind + ":" + query;
            if (offset == 0 || lastSearch?.Key != key)
            {
                var search = new SearchCursor(key);
                var document = await api.GetAsync($"searchResults?filter%5Bquery%5D={Uri.EscapeDataString(query)}&include={include}", token);
                if (document.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } searchResults)
                {
                    search.Items.AddRange(document.Related(searchResults, kind).Select(r => Convert(document, r, albumMode)));
                    search.Next = RelationshipNextLink(searchResults, kind);
                }
                lastSearch = search;
            }

            while (lastSearch.Items.Count < offset + limit && lastSearch.Next is { } next)
            {
                var document = await api.GetAsync(next.Contains("include=") ? next : next + "&include=" + include, token);
                lastSearch.Items.AddRange(document.Data.Select(document.Find).OfType<JsonElement>().Select(r => Convert(document, r, albumMode)));
                lastSearch.Next = document.NextLink;
            }
            return lastSearch.Items.Skip(offset).Take(limit).ToList();
        }
        finally
        {
            searchLock.Release();
        }
    }

    private static SongSearchObject Convert(JsonApiDocument document, JsonElement resource, bool album) =>
        album ? ConvertAlbum(document, resource) : ConvertTrack(document, resource);

    internal static SongSearchObject ConvertTrack(JsonApiDocument document, JsonElement track)
    {
        var album = document.Related(track, "albums").FirstOrDefault();
        var song = new SongSearchObject
        {
            Source = SourceIds.Tidal,
            Id = IdOf(track),
            Title = FullTitle(track),
            Artists = ArtistNames(document, track),
            AlbumName = album.ValueKind == JsonValueKind.Object ? FullTitle(album) : "",
            ReleaseDate = album.ValueKind == JsonValueKind.Object ? Attribute(album, "releaseDate") ?? "" : "",
            Duration = Seconds(Attribute(track, "duration")),
            Explicit = Attribute(track, "explicit") == "true",
            Isrc = Attribute(track, "isrc"),
            Rank = Popularity(track),
        };
        if (album.ValueKind == JsonValueKind.Object)
        {
            SetCovers(song, document, album);
        }
        return song;
    }

    internal static AlbumSearchObject ConvertAlbum(JsonApiDocument document, JsonElement album)
    {
        var result = new AlbumSearchObject
        {
            Source = SourceIds.Tidal,
            Id = IdOf(album),
            Title = FullTitle(album),
            AlbumName = FullTitle(album),
            Artists = ArtistNames(document, album),
            ReleaseDate = Attribute(album, "releaseDate") ?? "",
            Duration = Seconds(Attribute(album, "duration")),
            Explicit = Attribute(album, "explicit") == "true",
            Rank = Popularity(album),
            TracksCount = int.TryParse(Attribute(album, "numberOfItems"), out var count) ? count : 0,
            Upc = Attribute(album, "barcodeId"),
        };
        SetCovers(result, document, album);
        return result;
    }

    // TIDAL keeps a version apart from the title ("Get Lucky" + "feat. Pharrell Williams and Nile Rodgers"); the app
    // shows them together, as for Qobuz. Some titles already contain their version ("Random Access Memories (10th
    // Anniversary Edition)"), so it isn't added twice.
    private static string FullTitle(JsonElement resource)
    {
        var title = Attribute(resource, "title") ?? "";
        var version = Attribute(resource, "version");
        return string.IsNullOrWhiteSpace(version) || title.Contains(version, StringComparison.OrdinalIgnoreCase) ? title : $"{title} ({version})";
    }

    // In credit order.
    private static string ArtistNames(JsonApiDocument document, JsonElement resource) =>
        string.Join(", ", document.Related(resource, "artists").Select(a => Attribute(a, "name")).Where(n => !string.IsNullOrEmpty(n)));

    // Durations are ISO 8601 ("PT6M10S"); the app keeps whole seconds as text.
    internal static string Seconds(string? isoDuration)
    {
        if (string.IsNullOrEmpty(isoDuration))
        {
            return "0";
        }
        try
        {
            return ((int)Math.Round(XmlConvert.ToTimeSpan(isoDuration).TotalSeconds)).ToString();
        }
        catch (FormatException)
        {
            return "0";
        }
    }

    // TIDAL's popularity is 0 to 1; the app shows ranks as whole numbers.
    private static string Popularity(JsonElement resource) =>
        double.TryParse(Attribute(resource, "popularity"), System.Globalization.CultureInfo.InvariantCulture, out var popularity)
            ? ((int)Math.Round(popularity * 100)).ToString()
            : "0";

    // The cover comes in square sizes from 80 to 1280 px: 320 px for the lists, the largest for full-size use.
    private static void SetCovers(SongSearchObject item, JsonApiDocument document, JsonElement album)
    {
        var artwork = document.Related(album, "coverArt").FirstOrDefault();
        if (artwork.ValueKind != JsonValueKind.Object || AttributeElement(artwork, "files") is not { ValueKind: JsonValueKind.Array } files)
        {
            return;
        }
        var sizes = files.EnumerateArray()
            .Select(f => (Width: f.TryGetProperty("meta", out var meta) && meta.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                          Href: f.TryGetProperty("href", out var href) ? href.GetString() : null))
            .Where(f => f.Href != null)
            .OrderBy(f => f.Width)
            .ToList();
        if (sizes.Count == 0)
        {
            return;
        }
        item.ImageLocation = (sizes.FirstOrDefault(s => s.Width >= 320).Href ?? sizes[^1].Href)!;
        item.AdditionalFields = new Dictionary<string, object?> { ["cover_max"] = sizes[^1].Href };
    }
}
