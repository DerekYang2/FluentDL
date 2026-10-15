using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentDL.Helpers;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// RestHelper against a local server that always answers Deezer's rate-limit error. No internet.
/// </summary>
[TestClass]
public class RestHelperTests
{
    // Bug 11: a "Quota limit exceeded" answer was retried every 5 s forever, which could hang a search.
    [TestMethod]
    [Timeout(20000)]
    public async Task QuotaErrorsAreRetriedFiveTimesThenReturned()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var requests = 0;
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch (Exception) { return; }
                Interlocked.Increment(ref requests);
                var body = Encoding.UTF8.GetBytes("{\"error\":{\"type\":\"Exception\",\"message\":\"Quota limit exceeded\",\"code\":4}}");
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        });

        var rest = new RestHelper($"http://localhost:{port}") { QuotaDelay = TimeSpan.FromMilliseconds(10) };
        var result = await rest.FetchJsonElement("search?q=x");

        Assert.AreEqual(6, requests); // the first request and 5 retries
        StringAssert.Contains(result.ToString(), "Quota limit exceeded");
        listener.Stop();
        await server;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
