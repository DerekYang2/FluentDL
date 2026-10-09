using System.Text.RegularExpressions;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed class DeezerSource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource
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

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, CancellationToken token) =>
        CallbackResults.Match(callback => DeezerApi.GetDeezerTrack(song, token, callback));

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
}
