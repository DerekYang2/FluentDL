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
    internal class EmbedToken : IToken
    {
        public string AccessToken { get; set; } = default!;
        public string TokenType { get; set; } = "Bearer";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// The epoch time when the token expires.
        /// </summary>
        public DateTime ExpireTime { get; set; } = DateTime.UtcNow.AddSeconds(3600);
        public bool IsExpired { get => DateTime.UtcNow >= ExpireTime; }
    }
}