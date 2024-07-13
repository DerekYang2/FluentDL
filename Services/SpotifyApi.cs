using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace FluentDL.Services
{
    class SpotifyApi
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

        public async Task<FullPlaylist> GetPlaylist(string playlistId)
        {
            var playlist = await spotify.Playlists.Get(playlistId);
            // Debug: loop and print all tracks
            foreach (PlaylistTrack<IPlayableItem> item in playlist.Tracks.Items)
            {
                if (item.Track is FullTrack track)
                {
                    // All FullTrack properties are available
                    Debug.WriteLine(track.Name + " - " + track.Artists + " - " + track.Album.Name);
                }
            }

            return playlist;
        }
    }
}