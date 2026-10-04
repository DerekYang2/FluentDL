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
        // The check runs at startup while other services are connecting, so a first request can take several seconds.
        private static readonly RestClient client = new RestClient(new RestClientOptions("https://api.github.com") { Timeout = TimeSpan.FromSeconds(15) });

        public static async Task<Version?> GetLatestRelease() {
            // https://api.github.com/repos/derekyang2/fluentdl/releases/latest
            var req = "/repos/derekyang2/fluentdl/releases/latest";
            var request = new RestRequest(req);
            var response = await client.GetAsync(request);
            if (response.Content == null) return null;
            var rootElement = JsonDocument.Parse(response.Content).RootElement;
            var tag = rootElement.GetProperty("tag_name").GetString();
            return Version.TryParse(tag?.TrimStart('v', 'V'), out var version) ? version : null;
        }
    }
}
