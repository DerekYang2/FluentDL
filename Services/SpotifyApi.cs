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
        private SpotifyClientConfig config;
        private SpotifyClient spotify;
        private string clientId, clientSecret;

        public SpotifyApi(string clientId, string clientSecret)
        {
            config = SpotifyClientConfig.CreateDefault();
            this.clientId = clientId;
            this.clientSecret = clientSecret;
        }

        public async Task Initialize()
        {
            var request = new ClientCredentialsRequest(clientId, clientSecret);
            var response = await new OAuthClient(config).RequestToken(request);

            spotify = new SpotifyClient(config.WithToken(response.AccessToken));
        }

        public async Task<List<SongSearchObject>> GetPlaylist(string playlistId)
        {
            var playlist = await spotify.Playlists.Get(playlistId);
            var songs = new List<SongSearchObject>();
            // Debug: loop and print all tracks
            foreach (PlaylistTrack<IPlayableItem> item in playlist.Tracks.Items)
            {
                if (item.Track is FullTrack track)
                {
                    // All FullTrack properties are available
                    var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
                    songs.Add(new SongSearchObject
                    {
                        Source = "spotify",
                        Title = track.Name,
                        Artists = artistCsv,
                        ImageLocation = track.Album.Images[0].Url,
                        Id = track.Id,
                        ReleaseDate = track.Album.ReleaseDate,
                        Duration = SongSearchObject.FormatTime((int)(track.DurationMs / 1000.0)),
                        Rank = track.Popularity.ToString(),
                        AlbumName = track.Album.Name
                    });
                    Debug.WriteLine(songs.Last());
                }
            }

            return songs;
        }
    }
}