
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentDL.Helpers
{
    internal class RestHelper
    {
        private const int QuotaRetries = 5;

        private readonly RestClient client; 

        public RestHelper(string baseURL, int timeout = 5) {
            client = new RestClient(new RestClientOptions(baseURL) { Timeout = new TimeSpan(0, 0, timeout) });
        }

        /// <summary>How long to wait after a "Quota limit exceeded" answer. Tests shorten it.</summary>
        internal TimeSpan QuotaDelay { get; set; } = TimeSpan.FromSeconds(5);

        // A rate-limited request is retried a few times, then its answer is returned as is (bug 11: it used to retry
        // forever, which could hang a search).
        public async Task<JsonElement> FetchJsonElement(string req, CancellationToken token = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                var rootElement = await FetchOnce(req, token);
                if (attempt == QuotaRetries || !rootElement.ToString().Contains("Quota limit exceeded"))
                {
                    return rootElement;
                }

                try
                {
                    await Task.Delay(QuotaDelay, token);
                }
                catch (OperationCanceledException)
                {
                    return rootElement;
                }
            }
        }

        private async Task<JsonElement> FetchOnce(string req, CancellationToken token)
        {
            try
            {
                var request = new RestRequest(req);
                var response = await client.GetAsync(request, token);
                return JsonDocument.Parse(response.Content).RootElement;
            }
            catch (Exception e)
            {
                try
                {
                    Serilog.Log.Debug(e, "Provider request failed; trying the compatibility retry");
                    req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
                    var request = new RestRequest(req);
                    var response = await client.GetAsync(request, token);
                    return JsonDocument.Parse(response.Content).RootElement;
                }
                catch (Exception e2)
                {
                    Serilog.Log.Error(e2, "Provider request failed after the compatibility retry");
                    return new JsonElement();
                }
            }
        }
    }
}
