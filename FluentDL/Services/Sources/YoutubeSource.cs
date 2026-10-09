using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

/// <summary>YouTube needs no sign-in, so it has no session.</summary>
internal sealed class YoutubeSource : ISearchSource, ILinkSource, IConversionTarget, ICatalogSource
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

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, CancellationToken token) =>
        CallbackResults.Match(callback => YoutubeApi.GetYoutubeTrack(song, token, callback));

    // YouTube album results already hold full tracks.
    public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => Task.FromResult(album.TrackList ?? []);
}
