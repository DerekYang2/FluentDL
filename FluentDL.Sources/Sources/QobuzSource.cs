using FluentDL.Contracts.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed partial class QobuzSource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource, IDownloadSource, ITagSource
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Qobuz)!;

    private readonly ILocalSettingsService settings;

    public QobuzSource(ILocalSettingsService settings)
    {
        this.settings = settings;
    }

    public bool IsConnected => IsInitialized;

    public string? StatusText => LoginString();

    public async Task<ConnectResult> ConnectAsync()
    {
        var id = await settings.ReadSettingAsync<string>(SettingKeys.QobuzId);
        var token = await settings.ReadSettingAsync<string>(SettingKeys.QobuzToken);
        var appId = await settings.ReadSettingAsync<string>(SettingKeys.QobuzAppId);
        var appSecret = await settings.ReadSettingAsync<string>(SettingKeys.QobuzAppSecret);
        // Initialize blocks on network calls, so it runs off the caller's thread, as both callers do today.
        return await CallbackResults.Connect(
            callback => Task.Run(() => Initialize(id, token, appId, appSecret, callback)),
            () => IsInitialized);
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://www.qobuz.com/") || url.StartsWith("https://play.qobuz.com/") || url.StartsWith("https://open.qobuz.com/");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token)
    {
        if (IsLinkLabelOrArtist(url, out _, out _))
        {
            return AddTracksFromLabelOrArtist(results, url, token, status, request.Page.Offset, request.Page.Limit, request.AlbumMode);
        }
        return request.IsFirstPage ? AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;
    }

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false) =>
        CallbackResults.Match(callback => GetQobuzTrack(song, isrc, token, callback, isrcOnly));

    public Task<string> DownloadAsync(SongSearchObject track, string pathWithoutExtension, DownloadRequest request, IProgress<ProgressData>? progress)
    {
        var format = request.Quality switch
        {
            DownloadQuality.Mp3 => "5",
            DownloadQuality.Lossless => "6",
            _ => null, // the qobuz_quality setting
        };
        return DownloadTrack(pathWithoutExtension, track, progress!, format);
    }

    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
    {
        var albumInternal = await GetInternalAlbum(album.Id);
        albumInternal?.Tracks.Items.ForEach(t => t.Album = albumInternal);
        return albumInternal?.Tracks.Items.Select(t => ConvertSongSearchObject(t, albumInternal.Artist?.Name)).ToList() ?? [];
    }

    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        if (song is AlbumSearchObject selectedAlbum)
        {
            var album = await GetInternalAlbum(selectedAlbum.Id);
            var rows = new List<(string, string)>();
            if (album != null)
            {
                rows.Add(("Genre", string.Join(", ", PruneGenreList(album.GenresList))));
                rows.Add(("Max Quality", $"{album.MaximumBitDepth}-Bit/{album.MaximumSamplingRate} kHz"));
            }
            var tracks = album?.Tracks.Items.Select(t => ConvertSongSearchObject(t, album.Artist?.Name)).ToList() ?? [];
            selectedAlbum.TrackList = tracks;
            return new PreviewInfo
            {
                CoverUrl = album?.Image.Large,
                Rows = rows,
                RemoveAlbumRow = true,
                AlbumTracks = tracks,
                AlbumTrackCount = album?.TracksCount ?? 0,
            };
        }

        var track = await GetInternalTrack(song.Id);
        var trackRows = new List<(string, string)> { ("Track", song.TrackPosition) };
        if (track != null)
        {
            trackRows.Add(("Genre", string.Join(", ", PruneGenreList(track.Album.GenresList))));
            trackRows.Add(("Max Quality", $"{track.MaximumBitDepth}-Bit/{track.MaximumSamplingRate} kHz"));
        }
        if (!string.IsNullOrEmpty(song.Isrc))
        {
            trackRows.Add(("ISRC", song.Isrc!));
        }
        return new PreviewInfo { CoverUrl = track?.Album.Image.Large, Rows = trackRows, LoadAudioSeparately = true };
    }

    public Task<string?> GetPreviewAudioAsync(SongSearchObject track) => Task.Run(() => GetPreviewUri(track.Id));

    public Task<SongSearchObject?> GetTrackAsync(string id) => GetTrackAsync(id, CancellationToken.None);

    public async Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album)
    {
        var fullAlbumObj = await GetInternalAlbum(album.Id);
        if (!string.IsNullOrWhiteSpace(fullAlbumObj?.Image?.Mega))
        {
            return fullAlbumObj.Image.Mega;
        }
        if (!string.IsNullOrEmpty(fullAlbumObj?.Image?.Extralarge))
        {
            return fullAlbumObj.Image.Extralarge;
        }
        if (!string.IsNullOrEmpty(fullAlbumObj?.Image?.Large))
        {
            return fullAlbumObj.Image.Large;
        }
        return null;
    }

    public Task WriteTagsAsync(string filePath, SongSearchObject track) => UpdateMetadata(filePath, track.Id);
}
