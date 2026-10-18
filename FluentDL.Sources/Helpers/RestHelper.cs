
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Headers;

namespace FluentDL.Helpers
{
    internal class RestHelper
    {
        private readonly RestClient client; 
        private readonly ProviderRequestPolicy requests;

        public RestHelper(string baseURL, int timeout = 5) {
             client = new RestClient(new RestClientOptions(baseURL) { Timeout = new TimeSpan(0, 0, timeout) });
             requests = new Uri(baseURL).Host.Equals("api.deezer.com", StringComparison.OrdinalIgnoreCase)
                 ? ProviderRequests.Deezer : new ProviderRequestPolicy("REST", interval: TimeSpan.Zero, rateRetries: 5);
        }

        /// <summary>How long to wait after a "Quota limit exceeded" answer. Tests shorten it.</summary>
        internal TimeSpan QuotaDelay { get; set; } = TimeSpan.FromSeconds(5);

        public async Task<JsonElement> FetchJsonElement(string req, CancellationToken token = default)
        {
            try
            {
                return await FetchOnce(req, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                var compatible = req.Replace("%28", "", StringComparison.OrdinalIgnoreCase).Replace("%29", "", StringComparison.OrdinalIgnoreCase);
                if (compatible != req && (error is JsonException || error is HttpRequestException { StatusCode: HttpStatusCode.BadRequest }))
                {
                    try { return await FetchOnce(compatible, token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception retryError) { Serilog.Log.Debug(retryError, "Provider compatibility request failed"); }
                }
                Serilog.Log.Debug(error, "Provider catalog request failed after bounded retries");
                return default;
            }
        }

        private async Task<JsonElement> FetchOnce(string req, CancellationToken token)
        {
            var response = await requests.ExecuteAsync(async ct =>
            {
                var answer = await client.ExecuteAsync(new RestRequest(req, Method.Get), ct);
                ct.ThrowIfCancellationRequested();
                if (answer.ResponseStatus == ResponseStatus.TimedOut) throw new TimeoutException("Provider catalog request timed out.");
                if (answer.ResponseStatus != ResponseStatus.Completed)
                    throw new HttpRequestException("Provider catalog transport failed.", answer.ErrorException);
                return answer;
            }, Inspect, token);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new HttpRequestException("Provider catalog rejected the query.", null, response.StatusCode);
            using var document = JsonDocument.Parse(response.Content ?? "{}");
            return document.RootElement.Clone();
        }

        private RequestRetry Inspect(RestResponse response)
        {
            var header = response.Headers?.FirstOrDefault(value => string.Equals(value.Name, "Retry-After", StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
            RetryConditionHeaderValue.TryParse(header, out var retryAfter);
            var retry = RequestRetry.Http(response.StatusCode, retryAfter);
            if (retry.RateLimited || retry.Transient) return retry;
            try
            {
                using var json = JsonDocument.Parse(response.Content ?? "{}");
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && ((error.TryGetProperty("code", out var code) && code.ToString() == "4")
                        || (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                            && message.GetString()?.Contains("Quota limit exceeded", StringComparison.OrdinalIgnoreCase) == true)))
                    return new(true, false, retry.Delay ?? QuotaDelay);
            }
            catch (JsonException) { }
            return retry;
        }
    }
}
