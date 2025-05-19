using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentDL.Helpers
{
    /// <summary>
    /// Gets access token from embed page.
    /// </summary>
    internal class EmbedAuthenticator : IAuthenticator
    {
        // Some arbitrary embed, should work unless it is moved 
        private static readonly string EmbedTokenUrl = "https://open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M";
        private static readonly HttpClient httpClient = new();
        public EmbedToken? Token { get; private set; }
        public EmbedAuthenticator() : this(null) {}
        public EmbedAuthenticator(EmbedToken? token)
        {
            Token = token;
            // Check if embed works and user in logged into Spotify web player
            string htmlStr = httpClient.GetStringAsync(EmbedTokenUrl).GetAwaiter().GetResult();
            const string tokenStr = "\"accessToken\":\"";
            if (!htmlStr.Contains(tokenStr))
            {
                throw new Exception("Embed token not found in HTML string. Please log into Spotify web player.");
            }
            const string expireStr = "\"accessTokenExpirationTimestampMs\":";
            if (!htmlStr.Contains(expireStr))
            {
                throw new Exception("Expire time not found in HTML string. Please log into Spotify web player.");
            }
        }

        public async Task Apply(IRequest request, IAPIConnector apiConnector)
        {
            if (Token == null || Token.IsExpired)
            {
                string htmlStr = await httpClient.GetStringAsync(EmbedTokenUrl);
                // Find occurrence of audio preview
                const string tokenStr = "\"accessToken\":\"";
                var startIdx = htmlStr.IndexOf(tokenStr);
                if (startIdx == -1) throw new Exception("Token not found in HTML string.");
                var endIdx = htmlStr.IndexOf("\",", startIdx + tokenStr.Length);
                var token = htmlStr[(startIdx + tokenStr.Length)..endIdx];

                // Get expire time
                const string expireStr = "\"accessTokenExpirationTimestampMs\":";
                startIdx = htmlStr.IndexOf(expireStr);
                if (startIdx == -1) throw new Exception("Expire time not found in HTML string.");
                endIdx = htmlStr.IndexOf(',', startIdx + expireStr.Length);
                var expireTime = htmlStr[(startIdx + expireStr.Length)..endIdx];

                // Convert ms since epoch to DateTime
                var epoch = long.Parse(expireTime);
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime;

                // Debug.WriteLine(dateTime.ToString("yyyy-MM-dd HH:mm:ss") + " | now: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                Token = new EmbedToken
                {
                    AccessToken = token,
                    ExpireTime = dateTime,
                    CreatedAt = DateTime.UtcNow
                };
            }
            request.Headers["Authorization"] = $"{Token.TokenType} {Token.AccessToken}";
        }
    }
}