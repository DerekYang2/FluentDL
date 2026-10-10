using System.Text.RegularExpressions;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed class DeezerSource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Deezer)!;

    public bool IsConnected => DeezerApi.IsInitialized;

    public string? StatusText => DeezerApi.LoginString();

    public async Task<ConnectResult> ConnectAsync()
    {
        var arl = await SourceServices.Settings.ReadSettingAsync<string>(SettingKeys.DeezerArl);
        return await CallbackResults.Connect(callback => DeezerApi.InitDeezerClient(arl, callback), () => DeezerApi.IsInitialized);
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        DeezerApi.GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        DeezerApi.AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://deezer.page.link/") || url.StartsWith("https://dzr.page.link/") || url.StartsWith("https://link.deezer.com/")
        || Regex.IsMatch(url, @"https://www\.deezer\.com(/[^/]+)?/(track|album|playlist)/.*");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token) =>
        request.IsFirstPage ? DeezerApi.AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token) =>
        CallbackResults.Match(callback => DeezerApi.GetDeezerTrack(song, isrc, token, callback));

    // Search results carry only part of each track, so every track is loaded again.
    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
    {
        List<SongSearchObject> albumTracks = [];
        var fullAlbumObj = await DeezerApi.GetAlbum(album.Id);

        if (fullAlbumObj != null)
        {
            foreach (var t in fullAlbumObj.TrackList ?? [])
            {
                var track = await DeezerApi.GetTrack(t.Id);
                if (track != null)
                {
                    albumTracks.Add(track);
                }
            }
        }
        return albumTracks;
    }

    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        if (song is AlbumSearchObject album)
        {
            var albumJson = await DeezerApi.restClient.FetchJsonElement("album/" + album.Id);
            var fullAlbumObject = DeezerApi.GetAlbumFromJsonElement(albumJson);
            string? cover = null;
            if (albumJson.TryGetProperty("cover_big", out var coverBig) && !string.IsNullOrEmpty(coverBig.GetString()))
            {
                cover = coverBig.GetString();
            }
            var rows = new List<(string, string)> { ("Genre", DeezerApi.GetGenreStr(albumJson)), ("Max Quality", "16-Bit/44.1 kHz") };
            if (albumJson.TryGetProperty("fans", out var fans))
            {
                rows.Add(("Fans", fans.GetInt32().ToString()));
            }
            var tracks = fullAlbumObject?.TrackList ?? [];
            album.TrackList = tracks;
            return new PreviewInfo { CoverUrl = cover, Rows = rows, RemoveAlbumRow = true, AlbumTracks = tracks, AlbumTrackCount = album.TracksCount };
        }

        var json = await DeezerApi.restClient.FetchJsonElement("track/" + song.Id);
        var genre = await DeezerApi.GetGenreStr(json.GetProperty("album").GetProperty("id").GetInt32());
        var trackRows = new List<(string, string)> { ("Track", json.GetProperty("track_position").ToString()) };
        if (!string.IsNullOrEmpty(genre))
        {
            trackRows.Add(("Genre", genre));
        }
        trackRows.Add(("Max Quality", "16-Bit/44.1 kHz"));
        if (!string.IsNullOrEmpty(song.Isrc))
        {
            trackRows.Add(("ISRC", song.Isrc!));
        }
        return new PreviewInfo
        {
            CoverUrl = json.GetProperty("album").GetProperty("cover_big").ToString(),
            Rows = trackRows,
            Popularity = double.Parse(song.Rank) / 1E6, // rank runs from 0 to 1,000,000
            AudioUrl = json.GetProperty("preview").ToString(),
        };
    }

    public Task<string?> GetPreviewAudioAsync(SongSearchObject track) => Task.FromResult(track?.AdditionalFields?["preview"]?.ToString());
}
