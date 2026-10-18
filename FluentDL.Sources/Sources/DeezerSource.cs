using System.Text.RegularExpressions;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Playlists;

namespace FluentDL.Services.Sources;

internal sealed partial class DeezerSource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource, IDownloadSource, ITagSource, IAlbumUpcSource, IPlaylistWriter
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Deezer)!;

    private readonly ILocalSettingsService settings;
    private readonly IAudioConverter audioConverter;
    private readonly DeezerPlaylistClient playlists;

    public DeezerSource(ILocalSettingsService settings, IAudioConverter audioConverter) : this(settings, audioConverter, null) { }

    internal DeezerSource(ILocalSettingsService settings, IAudioConverter audioConverter, DeezerPlaylistClient? playlistClient)
    {
        this.settings = settings;
        this.audioConverter = audioConverter;
        playlists = playlistClient ?? new DeezerPlaylistClient(() => IsInitialized && deezerClient.GWApi.ActiveUserData?["checkForm"]?.ToString() is { Length: > 0 } apiToken
            ? new(deezerClient.ActiveARL, deezerClient.SID, apiToken, deezerClient, deezerClient.GWApi.ActiveUserData!["USER"]!["USER_ID"]!.ToString()) : null);
    }

    public IReadOnlyList<PlaylistVisibility> SupportedPlaylistVisibilities => DeezerPlaylistClient.Visibilities;
    public string PlaylistSignInHint => "Sign in at Settings > Authentication > Deezer with your ARL.";
    public int PlaylistNameMaxLength => 50;
    public Task<IReadOnlyList<OwnedPlaylist>> GetOwnedPlaylistsAsync(CancellationToken token) => playlists.GetOwnedAsync(token);
    public Task<IReadOnlyList<string>> GetPlaylistTrackIdsAsync(string playlistId, CancellationToken token) => playlists.GetOwnedTrackIdsAsync(playlistId, token);
    public Task<bool> CanCreatePlaylistAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(playlists.CanCreate); }
    public Task<PlaylistCreationResult> CreatePlaylistAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token) => playlists.CreateAsync(request, progress, token);

    public bool IsConnected => IsInitialized;

    public string? StatusText => LoginString();

    public async Task<ConnectResult> ConnectAsync()
    {
        var arl = await settings.ReadSettingAsync<string>(SettingKeys.DeezerArl);
        return await CallbackResults.Connect(callback => InitDeezerClient(arl, callback), () => IsInitialized);
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://deezer.page.link/") || url.StartsWith("https://dzr.page.link/") || url.StartsWith("https://link.deezer.com/")
        || Regex.IsMatch(url, @"https://www\.deezer\.com(/[^/]+)?/(track|album|playlist)/.*");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token) =>
        request.IsFirstPage ? AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false) =>
        CallbackResults.Match(callback => GetDeezerTrack(song, isrc, token, callback, isrcOnly));

    public Task<string> DownloadAsync(SongSearchObject track, string pathWithoutExtension, DownloadRequest request, IProgress<ProgressData>? progress)
    {
        DeezNET.Data.Bitrate? bitrate = request.Quality switch
        {
            DownloadQuality.Mp3 => request.Allow128Fallback ? DeezNET.Data.Bitrate.MP3_128 : DeezNET.Data.Bitrate.MP3_320,
            DownloadQuality.Lossless => DeezNET.Data.Bitrate.FLAC,
            _ => null, // the deezer_quality setting
        };
        return DownloadTrack(pathWithoutExtension, track, bitrate, request.Allow128Fallback, progress);
    }

    // Search results carry only part of each track, so every track is loaded again.
    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
    {
        List<SongSearchObject> albumTracks = [];
        var fullAlbumObj = await GetAlbum(album.Id);

        if (fullAlbumObj != null)
        {
            foreach (var t in fullAlbumObj.TrackList ?? [])
            {
                var track = await GetTrack(t.Id);
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
            var albumJson = await restClient.FetchJsonElement("album/" + album.Id);
            var fullAlbumObject = GetAlbumFromJsonElement(albumJson);
            string? cover = null;
            if (albumJson.TryGetProperty("cover_big", out var coverBig) && !string.IsNullOrEmpty(coverBig.GetString()))
            {
                cover = coverBig.GetString();
            }
            var rows = new List<(string, string)> { ("Genre", GetGenreStr(albumJson)), ("Max Quality", "16-Bit/44.1 kHz") };
            if (albumJson.TryGetProperty("fans", out var fans))
            {
                rows.Add(("Fans", fans.GetInt32().ToString()));
            }
            if ((albumJson.SafeGetString("upc") ?? album.Upc) is { Length: > 0 } upc)
            {
                rows.Add(("UPC", upc));
            }
            var tracks = fullAlbumObject?.TrackList ?? [];
            album.TrackList = tracks;
            return new PreviewInfo { CoverUrl = cover, Rows = rows, RemoveAlbumRow = true, AlbumTracks = tracks, AlbumTrackCount = album.TracksCount };
        }

        var json = await restClient.FetchJsonElement("track/" + song.Id);
        var genre = await GetGenreStr(json.GetProperty("album").GetProperty("id").GetInt32());
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

    public Task<SongSearchObject?> GetTrackAsync(string id) => GetTrack(id);

    // Read from the album itself, so it doesn't depend on which fields the album object carries.
    public async Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album)
    {
        var jsonObj = await restClient.FetchJsonElement("album/" + album.Id);
        var coverXl = jsonObj.SafeGetString("cover_xl");
        if (!string.IsNullOrEmpty(coverXl))
        {
            return coverXl;
        }
        var coverBig = jsonObj.SafeGetString("cover_big");
        return string.IsNullOrEmpty(coverBig) ? null : coverBig;
    }

    public Task WriteTagsAsync(string filePath, SongSearchObject track) => UpdateMetadata(filePath, track.Id);
}
