
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;

namespace FluentDL.Helpers
{
    internal class RestHelper
    {
        private readonly RestClient client; 

        public RestHelper(string baseURL, int timeout = 5) {
            client = new RestClient(new RestClientOptions(baseURL) { Timeout = new TimeSpan(0, 0, timeout) });
        }

        public async Task<JsonElement> FetchJsonElement(string req, CancellationToken token = default)
        {
            try
            {
                var request = new RestRequest(req);
                var response = await client.GetAsync(request, token);
                var rootElement = JsonDocument.Parse(response.Content).RootElement;
                if (rootElement.ToString().Contains("Quota limit exceeded")) // If the request is rate limited
                {
                    // wait 5 seconds and try again
                    await Task.Delay(5000);
                    return await FetchJsonElement(req, token);
                }

                return rootElement;
            }
            catch (Exception e)
            {
                try
                {
                    Debug.WriteLine("Failed: " + req);
                    req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
                    var request = new RestRequest(req);
                    var response = await client.GetAsync(request, token);
                    var rootElement = JsonDocument.Parse(response.Content).RootElement;

                    if (rootElement.ToString().Contains("Quota limit exceeded")) // If the request is rate limited
                    {
                        // wait 5 seconds and try again
                        await Task.Delay(5000);
                        return await FetchJsonElement(req, token);
                    }

                    return rootElement;
                }
                catch (Exception e2)
                {
                    Debug.WriteLine("Failed again: " + req);
                    Debug.WriteLine(e2);
                    return new JsonElement();
                }
            }
        }
    }
}
