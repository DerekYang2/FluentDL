using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

/// <summary>YouTube needs no sign-in, so it has no session.</summary>
internal sealed class YoutubeSource : ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.YouTube)!;

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        YoutubeApi.GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        YoutubeApi.AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://www.youtube.com/") || url.StartsWith("https://youtube.com/") || url.StartsWith("https://music.youtube.com/");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token) =>
        request.IsFirstPage ? YoutubeApi.AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;

    // YouTube matches by title and artist only, so the ISRC is not used.
    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token) =>
        CallbackResults.Match(callback => YoutubeApi.GetYoutubeTrack(song, token, callback));

    // YouTube album results already hold full tracks.
    public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => Task.FromResult(album.TrackList ?? []);

    // The preview pane loads YouTube audio itself, because it may use yt-dlp (an app setting) and shows progress.
    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        var cover = await YoutubeApi.GetMaxResThumbnail(song) ?? "";
        if (song is AlbumSearchObject album)
        {
            album.TrackList ??= [];
            return new PreviewInfo { CoverUrl = cover, AlbumTracks = album.TrackList, AlbumTrackCount = album.TracksCount };
        }
        return new PreviewInfo { CoverUrl = cover, Rows = [("Views", song.Rank)] };
    }

    // Used when yt-dlp is off.
    public async Task<string?> GetPreviewAudioAsync(SongSearchObject track) =>
        await YoutubeApi.AudioStreamWorstUrl("https://www.youtube.com/watch?v=" + track.Id);
}
