using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed class SpotifySource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Spotify)!;

    public bool IsConnected => SpotifyApi.IsInitialized;

    public string? StatusText => SpotifyApi.LoginString();

    public async Task<ConnectResult> ConnectAsync()
    {
        var clientId = await SourceServices.Settings.ReadSettingAsync<string>(SettingKeys.SpotifyClientId);
        var clientSecret = await SourceServices.Settings.ReadSettingAsync<string>(SettingKeys.SpotifyClientSecret);
        return await CallbackResults.Connect(callback => SpotifyApi.Initialize(clientId, clientSecret, callback), () => SpotifyApi.IsInitialized);
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        SpotifyApi.GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        SpotifyApi.AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) => url.StartsWith("https://open.spotify.com/");

    public async Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token)
    {
        if (!request.IsFirstPage)
        {
            return;
        }
        // With the prompt on, private playlists open a browser sign-in.
        var userLogin = await SourceServices.Settings.ReadSettingAsync<bool>(SettingKeys.SpotifyPlaylistPrompt);
        await SpotifyApi.AddTracksFromLink(results, url, token, status, userLogin, request.AlbumMode);
    }

    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, CancellationToken token) =>
        CallbackResults.Match(callback => SpotifyApi.GetSpotifyTrack(song, token, callback));
}
