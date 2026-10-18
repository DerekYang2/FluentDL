using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using FluentDL.Helpers;
using FluentDL.Core.Sources;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;
using System.Reflection;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class ProviderRequestPolicyTests
{
    private sealed class Clock
    {
        private readonly object gate = new();
        private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        internal DateTimeOffset Now { get { lock (gate) return now; } }
        internal Task Delay(TimeSpan amount, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (gate) now += amount;
            return Task.CompletedTask;
        }
    }

    private static ProviderRequestPolicy Policy(Clock clock, int rateRetries = 3, int transientRetries = 2) =>
        new("Test", interval: TimeSpan.Zero, rateRetries: rateRetries, transientRetries: transientRetries,
            attemptTimeout: Timeout.InfiniteTimeSpan, utcNow: () => clock.Now, delay: clock.Delay, jitter: () => 0);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    private static HttpResponseMessage Response(HttpStatusCode status) => new(status) { Content = new StringContent("{}") };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetryAfterAcceptsSecondsAndHttpDates(bool date)
    {
        var clock = new Clock();
        var start = clock.Now;
        var policy = Policy(clock);
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var response = Response(++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (calls == 1) response.Headers.RetryAfter = date ? new(clock.Now.AddSeconds(7)) : new(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        }));
        using var result = await policy.SendAsync(client, () => new(HttpMethod.Get, "https://example.test/tracks"), CancellationToken.None);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(2, calls);
        Assert.IsTrue(clock.Now - start >= TimeSpan.FromSeconds(7));
    }

    [TestMethod]
    public void PastRetryAfterDatesDoNotCreateNegativeDelays()
    {
        var now = DateTimeOffset.UtcNow;
        var retry = RequestRetry.Http(HttpStatusCode.TooManyRequests, new(now.AddSeconds(-10)), now);
        Assert.IsTrue(retry.RateLimited);
        Assert.AreEqual(TimeSpan.Zero, retry.Delay);
    }

    [TestMethod]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    public async Task PermanentHttpFailuresAreNotRetried(int code)
    {
        var policy = Policy(new Clock());
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response((HttpStatusCode)code)); }));
        using var response = await policy.SendAsync(client, () => new(HttpMethod.Get, "https://example.test/tracks"), CancellationToken.None);
        Assert.AreEqual(code, (int)response.StatusCode);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task RateRetriesAreBoundedAndFinalCooldownStillAppliesToOtherRequests()
    {
        var clock = new Clock();
        var start = clock.Now;
        var policy = Policy(clock);
        int calls = 0;
        using var result = await policy.ExecuteAsync(_ => Task.FromResult(Response(++calls <= 4 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)),
            response => RequestRetry.Http(response.StatusCode), CancellationToken.None);
        Assert.AreEqual(4, calls);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, result.StatusCode);
        using var next = await policy.ExecuteAsync(_ => { calls++; return Task.FromResult(Response(HttpStatusCode.OK)); },
            response => RequestRetry.Http(response.StatusCode), CancellationToken.None);
        Assert.AreEqual(5, calls);
        Assert.AreEqual(TimeSpan.FromSeconds(30), clock.Now - start);
    }

    [TestMethod]
    public async Task TemporaryServerErrorsRetryWithBackoff()
    {
        var clock = new Clock();
        var start = clock.Now;
        var policy = Policy(clock);
        int calls = 0;
        using var result = await policy.ExecuteAsync(_ => Task.FromResult(Response(++calls < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)),
            response => RequestRetry.Http(response.StatusCode), CancellationToken.None);
        Assert.AreEqual(3, calls);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(6), clock.Now - start);
    }

    [TestMethod]
    public async Task TransportFailuresCreateFreshRequestsAndStopAtTheRetryBudget()
    {
        var policy = Policy(new Clock());
        var requests = new List<HttpRequestMessage>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request);
            return requests.Count < 3 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Temporary transport failure."))
                : Task.FromResult(Response(HttpStatusCode.OK));
        }));
        using var result = await policy.SendAsync(client, () => new(HttpMethod.Get, "https://example.test/tracks"), CancellationToken.None);
        Assert.AreEqual(3, requests.Count);
        Assert.AreEqual(3, requests.Distinct().Count());
    }

    [TestMethod]
    public async Task ActualRequestTimeoutsRetryAndDoNotBecomeUserCancellation()
    {
        var policy = new ProviderRequestPolicy("Test", interval: TimeSpan.Zero, baseDelay: TimeSpan.Zero,
            attemptTimeout: TimeSpan.FromMilliseconds(20), jitter: () => 0);
        int calls = 0;
        try
        {
            await policy.ExecuteAsync<int>(async token => { calls++; await Task.Delay(Timeout.Infinite, token); return 0; }, _ => default, CancellationToken.None);
            Assert.Fail("The timeout should exhaust its retry budget.");
        }
        catch (TimeoutException) { }
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task UserCancellationNeverRetries()
    {
        var policy = Policy(new Clock());
        using var cancel = new CancellationTokenSource();
        int calls = 0;
        var task = policy.ExecuteAsync<int>(async token => { calls++; cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return 0; }, _ => default, cancel.Token);
        await Canceled(task);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task CancelingAQueuedWorkerDoesNotLeakAConcurrencySlot()
    {
        var policy = new ProviderRequestPolicy("Test", concurrency: 1, interval: TimeSpan.Zero, attemptTimeout: Timeout.InfiniteTimeSpan);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = policy.ExecuteAsync(async _ => { started.SetResult(); await finish.Task; return 1; }, _ => default, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancel = new CancellationTokenSource();
        bool ran = false;
        var queued = policy.ExecuteAsync(_ => { ran = true; return Task.FromResult(2); }, _ => default, cancel.Token);
        cancel.Cancel();
        await Canceled(queued);
        Assert.IsFalse(ran);
        finish.SetResult();
        await first;
        Assert.AreEqual(3, await policy.ExecuteAsync(_ => Task.FromResult(3), _ => default, CancellationToken.None));
    }

    [TestMethod]
    public async Task SharedCooldownStopsOtherWorkersButNotOtherProviders()
    {
        var clock = new Clock();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new ProviderRequestPolicy("TIDAL", interval: TimeSpan.Zero, attemptTimeout: Timeout.InfiniteTimeSpan,
            utcNow: () => clock.Now, jitter: () => 0, delay: async (duration, token) =>
            {
                waiting.TrySetResult();
                await resume.Task.WaitAsync(token);
                await clock.Delay(duration, token);
            });
        int firstCalls = 0, secondCalls = 0;
        var first = policy.ExecuteAsync(_ => Task.FromResult(Response(++firstCalls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)),
            response => new(response.StatusCode == HttpStatusCode.TooManyRequests, false, TimeSpan.FromSeconds(5)), CancellationToken.None);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = policy.ExecuteAsync(_ => { secondCalls++; return Task.FromResult(1); }, _ => default, CancellationToken.None);
        var independent = Policy(new Clock());
        Assert.AreEqual(9, await independent.ExecuteAsync(_ => Task.FromResult(9), _ => default, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(0, secondCalls);
        resume.SetResult();
        using var result = await first.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, await second.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task RequestStartsArePacedEvenWhenResponsesAreImmediate()
    {
        var clock = new Clock();
        var start = clock.Now;
        var policy = new ProviderRequestPolicy("Test", interval: TimeSpan.FromMilliseconds(200),
            utcNow: () => clock.Now, delay: clock.Delay, jitter: () => 0);
        for (int i = 0; i < 5; i++) await policy.ExecuteAsync(_ => Task.FromResult(1), _ => default, CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromMilliseconds(800), clock.Now - start);
    }

    [TestMethod]
    public async Task AThousandTrackBatchRespectsTheProviderConcurrencyCap()
    {
        var policy = new ProviderRequestPolicy("Test", concurrency: 2, interval: TimeSpan.Zero, attemptTimeout: Timeout.InfiniteTimeSpan);
        int active = 0, peak = 0, completed = 0;
        var tasks = Enumerable.Range(0, 1000).Select(_ => policy.ExecuteAsync(async token =>
        {
            int count = Interlocked.Increment(ref active);
            int previous;
            while ((previous = peak) < count && Interlocked.CompareExchange(ref peak, count, previous) != previous) { }
            await Task.Yield();
            Interlocked.Decrement(ref active);
            Interlocked.Increment(ref completed);
            return 1;
        }, _ => default, CancellationToken.None));
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1000, completed);
        Assert.IsTrue(peak <= 2, $"Observed {peak} concurrent requests.");
    }

    [TestMethod]
    [DataRow(429)]
    [DataRow(503)]
    public async Task MutatingCallsAreNeverReplayedWhenRetriesAreDisabled(int status)
    {
        var policy = Policy(new Clock());
        int calls = 0;
        using var result = await policy.ExecuteAsync(_ => { calls++; return Task.FromResult(Response((HttpStatusCode)status)); },
            response => RequestRetry.Http(response.StatusCode), CancellationToken.None, allowRetries: false);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task TidalCatalogRetriesTransportTimeoutsWithoutDiscardingAuthentication()
    {
        int auth = 0, catalog = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "auth.tidal.com")
            {
                auth++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"test-token","expires_in":14400,"token_type":"Bearer"}""") });
            }
            catalog++;
            return catalog == 1 ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("Transport timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""") });
        }));
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [SettingKeys.TidalClientId] = "test-id", [SettingKeys.TidalClientSecret] = "test-secret", [SettingKeys.TidalCountry] = "CA",
        });
        var api = new TidalApi(settings, client, requests: Policy(new Clock()));
        await api.GetAsync("tracks?filter%5Bisrc%5D=TEST", CancellationToken.None);
        Assert.AreEqual(1, auth);
        Assert.AreEqual(2, catalog);
    }

    [TestMethod]
    public async Task QobuzConversionMetadataUsesTheGovernedHttpClient()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            Assert.AreEqual("/api.json/0.2/track/get", request.RequestUri!.AbsolutePath);
            return calls == 1 ? Task.FromException<HttpResponseMessage>(new TimeoutException("Transport timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":23,"title":"Test track","isrc":"GBTEST000001","duration":180}""") });
        }));
        var source = new QobuzSource(new TidalParsingTests.DictionarySettings([])) { CatalogRequests = Policy(new Clock()) };
        typeof(QobuzSource).GetField("QobuzHttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, client);
        typeof(QobuzSource).GetField("HttpClientInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, true);
        var track = await source.GetTrackAsync("23", CancellationToken.None);
        Assert.AreEqual("23", track?.Id);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void ProviderBudgetsAreIndependent()
    {
        Assert.AreNotSame(ProviderRequests.Tidal, ProviderRequests.Qobuz);
        Assert.AreNotSame(ProviderRequests.Qobuz, ProviderRequests.Deezer);
    }

    private static async Task Canceled(Task task)
    {
        try { await task; Assert.Fail("The operation should have been cancelled."); }
        catch (OperationCanceledException) { }
    }
}
