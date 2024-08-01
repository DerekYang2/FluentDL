using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace FluentDL.Services
{
    internal class SpotifyApi
    {
        private static SpotifyClientConfig config = SpotifyClientConfig.CreateDefault();
        private static SpotifyClient spotify;

        public SpotifyApi()
        {
        }

        public static async Task Initialize()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var clientId = localSettings.Values["SpotifyClientId"];
            var clientSecret = localSettings.Values["SpotifyClientSecret"];

            // TODO: if do not exist, message should be shown
            if (clientId != null && clientSecret != null)
            {
                var request = new ClientCredentialsRequest(clientId.ToString(), clientSecret.ToString());
                var response = await new OAuthClient(config).RequestToken(request);

                spotify = new SpotifyClient(config.WithToken(response.AccessToken));
            }
        }

        public static async Task<string?> GetPlaylistName(string playlistId)
        {
            var playlist = await spotify.Playlists.Get(playlistId);
            var playlistName = playlist.Name;
            return playlistName;
        }

        // TODO: handle invalid playlist ids
        public static async Task<List<SongSearchObject>> GetPlaylist(string playlistId)
        {
            var pages = await spotify.Playlists.GetItems(playlistId);
            var allPages = await spotify.PaginateAll(pages);

            var songs = new List<SongSearchObject>();
            // Debug: loop and print all tracks
            foreach (PlaylistTrack<IPlayableItem> item in allPages)
            {
                if (item.Track is FullTrack track)
                {
                    // All FullTrack properties are available
                    var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
                    if (artistCsv.Length == 0 || track.Album.Name.Length == 0)
                    {
                        continue;
                    }

                    songs.Add(new SongSearchObject
                    {
                        Source = "spotify",
                        Title = track.Name,
                        Artists = artistCsv,
                        ImageLocation = track.Album.Images[0].Url,
                        Id = track.Id,
                        ReleaseDate = track.Album.ReleaseDate,
                        Duration = ((int)Math.Round(track.DurationMs / 1000.0)).ToString(),
                        Rank = track.Popularity.ToString(),
                        AlbumName = track.Album.Name,
                        Explicit = track.Explicit,
                        TrackPosition = track.TrackNumber.ToString()
                    });
                }
            }

            return songs;
        }
    }
}