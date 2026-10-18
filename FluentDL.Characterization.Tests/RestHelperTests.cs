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

    [TestMethod]
    public async Task Http429UsesRetryAfterAndRecovers()
    {
        await using var server = new Server(count => (count < 3 ? 429 : 200, count < 3 ? "{}" : "{\"data\":[]}", "0"));
        var result = await new RestHelper(server.Address).FetchJsonElement("search?q=test");
        Assert.AreEqual(3, server.Requests);
        Assert.IsTrue(result.TryGetProperty("data", out _));
    }

    [TestMethod]
    public async Task QuotaCooldownStopsImmediatelyWhenTheUserCancels()
    {
        await using var server = new Server(_ => (200, "{\"error\":{\"code\":4,\"message\":\"Quota limit exceeded\"}}", null));
        var helper = new RestHelper(server.Address) { QuotaDelay = TimeSpan.FromMinutes(1) };
        using var cancellation = new CancellationTokenSource();
        var operation = helper.FetchJsonElement("search?q=test", cancellation.Token);
        try
        {
            await server.First.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            try { await operation; Assert.Fail("The quota wait should be cancelled."); }
            catch (OperationCanceledException) { }
            Assert.AreEqual(1, server.Requests);
        }
        finally { cancellation.Cancel(); }
    }

    [TestMethod]
    public async Task TheCompatibilityRetryOnlyRunsForABadEncodedQuery()
    {
        await using var server = new Server(count => (count == 1 ? 400 : 200, count == 1 ? "{\"error\":\"bad query\"}" : "{\"data\":[]}", null));
        var result = await new RestHelper(server.Address).FetchJsonElement("search?q=%28test%29");
        Assert.AreEqual(2, server.Requests);
        Assert.IsTrue(result.TryGetProperty("data", out _));
    }

    [TestMethod]
    public async Task ATrackTitleContainingQuotaTextIsNotTreatedAsAQuotaError()
    {
        await using var server = new Server(_ => (200, "{\"title\":\"Quota limit exceeded\"}", null));
        var result = await new RestHelper(server.Address).FetchJsonElement("track/1");
        Assert.AreEqual(1, server.Requests);
        Assert.AreEqual("Quota limit exceeded", result.GetProperty("title").GetString());
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task loop;
        private int requests;
        internal int Requests => requests;
        internal string Address { get; }
        internal TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Server(Func<int, (int Status, string Body, string? RetryAfter)> answer)
        {
            Address = $"http://localhost:{FreePort()}/";
            listener.Prefixes.Add(Address);
            listener.Start();
            loop = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch (Exception) when (!listener.IsListening) { break; }
                    var response = answer(Interlocked.Increment(ref requests));
                    context.Response.StatusCode = response.Status;
                    context.Response.ContentType = "application/json";
                    if (response.RetryAfter != null) context.Response.Headers.Add("Retry-After", response.RetryAfter);
                    var bytes = Encoding.UTF8.GetBytes(response.Body);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                    First.TrySetResult();
                }
            });
        }
        public async ValueTask DisposeAsync() { listener.Stop(); await loop; listener.Close(); }
    }
}
