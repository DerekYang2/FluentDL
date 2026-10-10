using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

internal sealed class SpotifySource : ISourceSession, ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource, IIsrcSource
{
    private static readonly HttpClient httpClient = new();

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

    // GetSpotifyTrack reads song.Isrc itself and never used a looked-up ISRC, so the passed one is ignored.
    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token) =>
        CallbackResults.Match(callback => SpotifyApi.GetSpotifyTrack(song, token, callback));

    public Task<string?> GetIsrcAsync(string trackId) => SpotifyApi.GetIsrcFromId(trackId);

    // The album search result already lists its tracks; each one is loaded in full.
    public async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
    {
        List<SongSearchObject> albumTracks = [];
        foreach (var t in album.TrackList ?? [])
        {
            var songObj = await SpotifyApi.GetTrack(t.Id);
            if (songObj != null)
            {
                albumTracks.Add(songObj);
            }
        }
        return albumTracks;
    }

    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        if (song is AlbumSearchObject album)
        {
            return new PreviewInfo
            {
                CoverUrl = (string)(album.AdditionalFields?["cover_max"] ?? ""), // the largest
                RemoveAlbumRow = true,
                AlbumTracks = album.TrackList ?? [],
                AlbumTrackCount = album.TracksCount,
            };
        }

        var track = await SpotifyApi.GetTrack(song.Id);
        var rows = new List<(string, string)> { ("Track", track?.TrackPosition ?? song.TrackPosition) };
        if (track?.Rank != null && long.TryParse(track.Rank, out long plays) && plays > 0)
        {
            rows.Add(("Play Count", YoutubeApi.FormatLargeValue(plays)));
        }
        if (!string.IsNullOrEmpty(track?.Isrc))
        {
            rows.Add(("ISRC", track.Isrc!));
        }

        var hasPreviewUrl = track?.AdditionalFields?.ContainsKey("preview_url") ?? false;
        return new PreviewInfo
        {
            CoverUrl = track?.ImageLocation,
            ReleaseDate = track?.ReleaseDate ?? song.ReleaseDate,
            Rows = rows,
            AudioUrl = hasPreviewUrl ? track!.AdditionalFields["preview_url"]?.ToString() : null,
            LoadAudioSeparately = !hasPreviewUrl,
        };
    }

    // Reads the preview link from the track's embed page.
    public async Task<string?> GetPreviewAudioAsync(SongSearchObject track)
    {
        try
        {
            string htmlStr = await httpClient.GetStringAsync($"https://open.spotify.com/embed/track/{track.Id}");
            return SpotifyApi.GetSpotifyPreviewUrl(htmlStr);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("Failed to get spotify preview url: " + e.Message);
        }
        return null;
    }
}
