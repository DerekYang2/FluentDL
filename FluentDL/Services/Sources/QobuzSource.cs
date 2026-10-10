using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed class QobuzSource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Qobuz)!;

    public bool IsConnected => QobuzApi.IsInitialized;

    public string? StatusText => QobuzApi.LoginString();

    public async Task<ConnectResult> ConnectAsync()
    {
        var settings = SourceServices.Settings;
        var id = await settings.ReadSettingAsync<string>(SettingKeys.QobuzId);
        var token = await settings.ReadSettingAsync<string>(SettingKeys.QobuzToken);
        var appId = await settings.ReadSettingAsync<string>(SettingKeys.QobuzAppId);
        var appSecret = await settings.ReadSettingAsync<string>(SettingKeys.QobuzAppSecret);
        // Initialize blocks on network calls, so it runs off the caller's thread, as both callers do today.
        return await CallbackResults.Connect(
            callback => Task.Run(() => QobuzApi.Initialize(id, token, appId, appSecret, callback)),
            () => QobuzApi.IsInitialized);
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        QobuzApi.GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        QobuzApi.AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://www.qobuz.com/") || url.StartsWith("https://play.qobuz.com/") || url.StartsWith("https://open.qobuz.com/");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token)
    {
        if (QobuzApi.IsLinkLabelOrArtist(url, out _, out _))
        {
            return QobuzApi.AddTracksFromLabelOrArtist(results, url, token, status, request.Page.Offset, request.Page.Limit, request.AlbumMode);
        }
        return request.IsFirstPage ? QobuzApi.AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;
    }

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token) =>
        CallbackResults.Match(callback => QobuzApi.GetQobuzTrack(song, isrc, token, callback));

    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
    {
        var albumInternal = await QobuzApi.GetInternalAlbum(album.Id);
        albumInternal?.Tracks.Items.ForEach(t => t.Album = albumInternal);
        return albumInternal?.Tracks.Items.Select(t => QobuzApi.ConvertSongSearchObject(t, albumInternal.Artist?.Name)).ToList() ?? [];
    }

    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        if (song is AlbumSearchObject selectedAlbum)
        {
            var album = await QobuzApi.GetInternalAlbum(selectedAlbum.Id);
            var rows = new List<(string, string)>();
            if (album != null)
            {
                rows.Add(("Genre", string.Join(", ", QobuzApi.PruneGenreList(album.GenresList))));
                rows.Add(("Max Quality", $"{album.MaximumBitDepth}-Bit/{album.MaximumSamplingRate} kHz"));
            }
            var tracks = album?.Tracks.Items.Select(t => QobuzApi.ConvertSongSearchObject(t, album.Artist?.Name)).ToList() ?? [];
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

        var track = await QobuzApi.GetInternalTrack(song.Id);
        var trackRows = new List<(string, string)> { ("Track", song.TrackPosition) };
        if (track != null)
        {
            trackRows.Add(("Genre", string.Join(", ", QobuzApi.PruneGenreList(track.Album.GenresList))));
            trackRows.Add(("Max Quality", $"{track.MaximumBitDepth}-Bit/{track.MaximumSamplingRate} kHz"));
        }
        if (!string.IsNullOrEmpty(song.Isrc))
        {
            trackRows.Add(("ISRC", song.Isrc!));
        }
        return new PreviewInfo { CoverUrl = track?.Album.Image.Large, Rows = trackRows, LoadAudioSeparately = true };
    }

    public Task<string?> GetPreviewAudioAsync(SongSearchObject track) => Task.Run(() => QobuzApi.GetPreviewUri(track.Id));
}
