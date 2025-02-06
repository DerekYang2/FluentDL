using Org.BouncyCastle.Ocsp;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentDL.Helpers
{
    class GithubAPI
    {
        private static readonly RestClient client = new RestClient(new RestClientOptions("https://api.github.com") { Timeout = new TimeSpan(0, 0, 5) });

        public static async Task<string?> GetLatestRelease() {
            // https://api.github.com/repos/derekyang2/fluentdl/releases/latest
            var req = "/repos/derekyang2/fluentdl/releases/latest";
            var request = new RestRequest(req);
            var response = await client.GetAsync(request);
            if (response.Content == null) return null;
            var rootElement = JsonDocument.Parse(response.Content).RootElement;
            return rootElement.GetProperty("tag_name").GetString();
        }
    }
}
