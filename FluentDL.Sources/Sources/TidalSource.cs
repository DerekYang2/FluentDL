using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
internal sealed class TidalSource : ISourceSession, ISearchSource, ILinkSource, ICatalogSource, IPreviewSource
{
    private const string TrackIncludes = "tracks,tracks.artists,tracks.albums.coverArt";
    private const string AlbumIncludes = "albums,albums.artists,albums.coverArt";
    private const string TrackDetailIncludes = "artists,albums,albums.coverArt";

    // tidal.com/track/1, tidal.com/browse/track/1, listen.tidal.com/track/1 (which redirects to tidal.com), for
    // tracks, albums and playlists (whose ids are UUIDs).
    private static readonly Regex LinkPattern = new(@"^https?://(?:www\.|listen\.)?tidal\.com/(?:browse/)?(track|album|playlist)/([A-Za-z0-9-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    public bool CanHandle(string url) => LinkPattern.IsMatch(url.Trim());

    // Links load everything on the first page.
    public async Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token)
    {
        var match = LinkPattern.Match(url.Trim());
        if (!request.IsFirstPage || !match.Success)
        {
            return;
        }
        var (kind, id) = (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value);

        try
        {
            switch (kind)
            {
                case "track":
                    if (await GetTrackAsync(id, token) is { } track)
                    {
                        results.Add(track);
                        status?.Invoke(StatusLevel.Success, $"<b>TIDAL</b>   Loaded track <a href='{url}'>{track.Title}</a>");
                    }
                    else
                    {
                        status?.Invoke(StatusLevel.Error, $"<b>TIDAL</b>   No track found at <a href='{url}'>{url}</a>");
                    }
                    break;

                case "album":
                    if (await GetAlbumAsync(id, token) is not { } album)
                    {
                        status?.Invoke(StatusLevel.Error, $"<b>TIDAL</b>   No album found at <a href='{url}'>{url}</a>");
                    }
                    else if (request.AlbumMode)
                    {
                        results.Add(album);
                        status?.Invoke(StatusLevel.Success, $"<b>TIDAL</b>   Loaded album <a href='{url}'>{album.Title}</a>");
                    }
                    else
                    {
                        status?.Invoke(StatusLevel.Informational, $"<b>TIDAL</b>   Loading album <a href='{url}'>{album.Title}</a>", -1);
                        foreach (var albumTrack in await GetAlbumTracksAsync(album, token))
                        {
                            results.Add(albumTrack);
                        }
                        status?.Invoke(StatusLevel.Success, $"<b>TIDAL</b>   Loaded album <a href='{url}'>{album.Title}</a>");
                    }
                    break;

                case "playlist":
                    status?.Invoke(StatusLevel.Informational, $"<b>TIDAL</b>   Loading playlist <a href='{url}'>{url}</a>", -1);
                    var (name, tracks) = await GetPlaylistAsync(id, token);
                    foreach (var playlistTrack in tracks)
                    {
                        results.Add(playlistTrack);
                    }
                    status?.Invoke(StatusLevel.Success, $"<b>TIDAL</b>   Loaded playlist <a href='{url}'>{name}</a>");
                    break;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status?.Invoke(StatusLevel.Error, $"<b>TIDAL</b>   Error loading <a href='{url}'>{url}</a>: {e.Message}");
        }
    }

    public Task<SongSearchObject?> GetTrackAsync(string id) => GetTrackAsync(id, CancellationToken.None);

    /// <summary>A track with its artists, album and cover, or null when TIDAL has no track with this id here.</summary>
    public async Task<SongSearchObject?> GetTrackAsync(string id, CancellationToken token)
    {
        var document = await GetOrNullAsync($"tracks/{Uri.EscapeDataString(id)}?include={TrackDetailIncludes}", token);
        return document?.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } track ? ConvertTrack(document, track) : null;
    }

    /// <summary>An album with its artists and cover, or null when TIDAL has no album with this id here.</summary>
    public async Task<AlbumSearchObject?> GetAlbumAsync(string id, CancellationToken token)
    {
        var document = await GetOrNullAsync($"albums/{Uri.EscapeDataString(id)}?include=artists,coverArt", token);
        return document?.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } album ? ConvertAlbum(document, album) : null;
    }

    public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => GetAlbumTracksAsync(album, CancellationToken.None);

    /// <summary>
    /// Every track of an album, in order, with its track number. The album's tracks come 20 a page; each takes the
    /// album's name, date and cover from the album itself.
    /// </summary>
    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album, CancellationToken token)
    {
        var document = await GetOrNullAsync($"albums/{Uri.EscapeDataString(album.Id)}?include=items,items.artists,artists,coverArt", token);
        if (document?.Data.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } albumResource)
        {
            return [];
        }
        var albumObject = ConvertAlbum(document, albumResource);
        var tracks = new List<SongSearchObject>();
        foreach (var (itemDocument, reference, resource) in await AllRelatedAsync(document, albumResource, "items", "items,items.artists", token))
        {
            if (TypeOf(resource) != "tracks")
            {
                continue;
            }
            var track = ConvertTrack(itemDocument, resource);
            track.AlbumName = albumObject.Title;
            track.ReleaseDate = albumObject.ReleaseDate;
            track.ImageLocation = albumObject.ImageLocation;
            track.AdditionalFields = albumObject.AdditionalFields;
            if (reference.TryGetProperty("meta", out var meta) && meta.TryGetProperty("trackNumber", out var number))
            {
                track.TrackPosition = number.GetRawText();
            }
            tracks.Add(track);
        }
        return tracks;
    }

    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        var rows = new List<(string, string)>();
        if (song.AdditionalFields?.GetValueOrDefault("max_quality") is string quality)
        {
            rows.Add(("Max Quality", quality));
        }
        var popularity = double.TryParse(song.Rank, out var rank) ? rank / 100 : (double?)null;
        var cover = song.AdditionalFields?.GetValueOrDefault("cover_max") as string ?? song.ImageLocation;

        if (song is AlbumSearchObject album)
        {
            if (!string.IsNullOrEmpty(album.Upc))
            {
                rows.Add(("UPC", album.Upc!));
            }
            var tracks = await GetAlbumTracksAsync(album, CancellationToken.None);
            album.TrackList = tracks;
            return new PreviewInfo { CoverUrl = cover, Rows = rows, RemoveAlbumRow = true, Popularity = popularity, AlbumTracks = tracks, AlbumTrackCount = album.TracksCount };
        }

        if (!string.IsNullOrEmpty(song.Isrc))
        {
            rows.Add(("ISRC", song.Isrc!));
        }
        return new PreviewInfo { CoverUrl = cover, Rows = rows, Popularity = popularity, LoadAudioSeparately = true };
    }

    /// <summary>
    /// The track's 30 s preview, as the address of an HLS playlist (a list of short AAC chunks that Windows' media player
    /// streams). TIDAL gives a preview, not the full track, without a subscription login. The address expires, so it
    /// is fetched when the preview opens.
    /// </summary>
    public async Task<string?> GetPreviewAudioAsync(SongSearchObject track)
    {
        var document = await GetOrNullAsync(
            $"trackManifests/{Uri.EscapeDataString(track.Id)}?manifestType=HLS&formats=HEAACV1&uriScheme=HTTPS&usage=PLAYBACK&adaptive=false",
            CancellationToken.None);
        return document?.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } manifest ? Attribute(manifest, "uri") : null;
    }

    /// <summary>A public playlist's name and tracks, in order. Videos in the playlist are left out.</summary>
    public async Task<(string Name, List<SongSearchObject> Tracks)> GetPlaylistAsync(string id, CancellationToken token)
    {
        const string itemIncludes = "items,items.artists,items.albums.coverArt";
        var document = await GetOrNullAsync($"playlists/{Uri.EscapeDataString(id)}?include={itemIncludes}", token);
        if (document?.Data.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } playlist)
        {
            throw new InvalidOperationException("TIDAL has no public playlist with this id.");
        }
        var tracks = (await AllRelatedAsync(document, playlist, "items", itemIncludes, token))
            .Where(item => TypeOf(item.Resource) == "tracks")
            .Select(item => ConvertTrack(item.Document, item.Resource))
            .ToList();
        return (Attribute(playlist, "name") ?? id, tracks);
    }

    public async Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) =>
        album.AdditionalFields?.GetValueOrDefault("cover_max") as string
        ?? (await GetAlbumAsync(album.Id, CancellationToken.None))?.AdditionalFields?.GetValueOrDefault("cover_max") as string;

    /// <summary>
    /// The track with this ISRC. TIDAL can list one recording on several releases (album, single, compilations, a
    /// hi-res reissue); the one on <paramref name="albumName"/> is preferred, else the first.
    /// </summary>
    public async Task<SongSearchObject?> GetTrackByIsrcAsync(string isrc, string? albumName, CancellationToken token)
    {
        var document = await api.GetAsync($"tracks?filter%5Bisrc%5D={Uri.EscapeDataString(isrc)}&include={TrackDetailIncludes}", token);
        var tracks = document.Data.Select(t => ConvertTrack(document, t)).Where(t => string.Equals(t.Isrc, isrc, StringComparison.OrdinalIgnoreCase)).ToList();
        return tracks.FirstOrDefault(t => !string.IsNullOrEmpty(albumName) && FuzzyMatch.CloseMatch(t.AlbumName, albumName)) ?? tracks.FirstOrDefault();
    }

    /// <summary>The album with this UPC (barcode), or null.</summary>
    public async Task<AlbumSearchObject?> GetAlbumByUpcAsync(string upc, CancellationToken token)
    {
        var document = await api.GetAsync($"albums?filter%5BbarcodeId%5D={Uri.EscapeDataString(upc)}&include=artists,coverArt", token);
        return document.Data.FirstOrDefault() is { ValueKind: JsonValueKind.Object } album ? ConvertAlbum(document, album) : null;
    }

    // A missing resource is a 404; anything else is an error.
    private async Task<JsonApiDocument?> GetOrNullAsync(string pathAndQuery, CancellationToken token)
    {
        try
        {
            return await api.GetAsync(pathAndQuery, token);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // The resources a to-many relationship points to, following its next links (20 a page). Each comes with its
    // reference, whose meta carries album track numbers, and the response it came in, which holds its artists.
    private async Task<List<(JsonApiDocument Document, JsonElement Reference, JsonElement Resource)>> AllRelatedAsync(
        JsonApiDocument first, JsonElement owner, string relationship, string pageIncludes, CancellationToken token)
    {
        var items = new List<(JsonApiDocument, JsonElement, JsonElement)>();
        foreach (var reference in References(owner, relationship))
        {
            if (first.Find(reference) is { } resource)
            {
                items.Add((first, reference, resource));
            }
        }
        var next = RelationshipNextLink(owner, relationship);
        while (next != null)
        {
            var page = await api.GetAsync(next.Contains("include=") ? next : next + "&include=" + pageIncludes, token);
            foreach (var reference in page.Data)
            {
                if (page.Find(reference) is { } resource)
                {
                    items.Add((page, reference, resource));
                }
            }
            next = page.NextLink;
        }
        return items;
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
        SetMaxQuality(song, track);
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
        SetMaxQuality(result, album);
        return result;
    }

    // TIDAL tags what a track or album is available in. Only shown in the preview: FluentDL doesn't download from TIDAL.
    private static void SetMaxQuality(SongSearchObject item, JsonElement resource)
    {
        if (AttributeElement(resource, "mediaTags") is not { ValueKind: JsonValueKind.Array } tags)
        {
            return;
        }
        var names = tags.EnumerateArray().Select(t => t.GetString()).ToHashSet();
        var quality = names.Contains("HIRES_LOSSLESS") ? "Hi-Res Lossless (up to 24-bit/192 kHz)"
            : names.Contains("LOSSLESS") ? "Lossless (16-bit/44.1 kHz)"
            : null;
        if (quality != null)
        {
            (item.AdditionalFields ??= [])["max_quality"] = quality;
        }
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
