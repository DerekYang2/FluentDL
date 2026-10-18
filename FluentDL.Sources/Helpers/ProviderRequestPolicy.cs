using System.Net;
using System.Net.Http.Headers;

namespace FluentDL.Helpers;

internal readonly record struct RequestRetry(bool RateLimited, bool Transient, TimeSpan? Delay = null)
{
    internal static RequestRetry Http(HttpStatusCode status, RetryConditionHeaderValue? retryAfter = null, DateTimeOffset? now = null)
    {
        TimeSpan? delay = retryAfter?.Delta;
        if (delay == null && retryAfter?.Date is { } date) delay = date - (now ?? DateTimeOffset.UtcNow);
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        return new(status == HttpStatusCode.TooManyRequests,
            status is HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout, delay);
    }
}

// One budget per provider, shared by catalog conversion requests. These are conservative app defaults,
// not claimed provider quotas. Auth requests and audio transfers keep their existing transports.
internal static class ProviderRequests
{
    internal static ProviderRequestPolicy Tidal { get; } = new("TIDAL");
    internal static ProviderRequestPolicy Qobuz { get; } = new("Qobuz");
    internal static ProviderRequestPolicy Deezer { get; } = new("Deezer", rateRetries: 5);
    internal static ProviderRequestPolicy SpotifyPlaylists { get; } = new("Spotify playlists");
}

internal sealed class ProviderRequestPolicy
{
    private readonly string provider;
    private readonly SemaphoreSlim slots;
    private readonly object gate = new();
    private readonly TimeSpan interval;
    private readonly TimeSpan baseDelay;
    private readonly TimeSpan attemptTimeout;
    private readonly int rateRetries;
    private readonly int transientRetries;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<double> jitter;
    private DateTimeOffset nextStart;
    private DateTimeOffset cooldown;

    internal ProviderRequestPolicy(string provider, int concurrency = 2, TimeSpan? interval = null,
        int rateRetries = 3, int transientRetries = 2, TimeSpan? baseDelay = null, TimeSpan? attemptTimeout = null,
        Func<DateTimeOffset>? utcNow = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<double>? jitter = null)
    {
        if (concurrency < 1 || rateRetries < 0 || transientRetries < 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        this.provider = provider;
        slots = new(concurrency, concurrency);
        this.interval = interval ?? TimeSpan.FromMilliseconds(200);
        this.baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
        this.attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(30);
        if (this.interval < TimeSpan.Zero || this.baseDelay < TimeSpan.Zero
            || (this.attemptTimeout <= TimeSpan.Zero && this.attemptTimeout != Timeout.InfiniteTimeSpan))
            throw new ArgumentOutOfRangeException(nameof(interval));
        this.rateRetries = rateRetries;
        this.transientRetries = transientRetries;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        this.jitter = jitter ?? (() => Random.Shared.NextDouble());
    }

    internal Task<HttpResponseMessage> SendAsync(HttpClient client, Func<HttpRequestMessage> request,
        CancellationToken token, bool allowRetries = true) => ExecuteAsync(async ct =>
        {
            using var message = request();
            return await client.SendAsync(message, ct).ConfigureAwait(false);
        }, response => RequestRetry.Http(response.StatusCode, response.Headers.RetryAfter, utcNow()), token, allowRetries);

    internal async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, Func<T, RequestRetry> classify,
        CancellationToken token, bool allowRetries = true)
    {
        int rates = 0, transients = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan? retryDelay = null;
            using (await EnterAsync(token).ConfigureAwait(false))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // Mutating playlist requests are sent once and retain HttpClient's existing timeout.
                if (allowRetries && attemptTimeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(attemptTimeout);
                T result;
                try
                {
                    result = await operation(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (allowRetries && !token.IsCancellationRequested && IsTransient(error))
                {
                    var wait = Backoff(transients + 1);
                    Pause(wait);
                    if (transients++ >= transientRetries)
                    {
                        if (error is OperationCanceledException)
                            throw new TimeoutException($"{provider} catalog request timed out after bounded retries.", error);
                        throw;
                    }
                    retryDelay = wait;
                    Serilog.Log.Warning("{Provider} catalog transport failed with {Failure}; retry {Retry} after {DelaySeconds:0.##} s",
                        provider, error.GetType().Name, transients, retryDelay.Value.TotalSeconds);
                    result = default!;
                }
                if (retryDelay == null)
                {
                    RequestRetry retry;
                    try { retry = classify(result); }
                    catch
                    {
                        if (result is IDisposable disposable) disposable.Dispose();
                        throw;
                    }
                    if (retry.RateLimited)
                    {
                        var wait = retry.Delay ?? Backoff(rates + 1);
                        Pause(wait);
                        if (!allowRetries || rates++ >= rateRetries) return result;
                        Serilog.Log.Warning("{Provider} catalog rate limited; retry {Retry} after a shared cooldown of {DelaySeconds:0.##} s",
                            provider, rates, wait.TotalSeconds);
                        if (result is IDisposable disposable) disposable.Dispose();
                        // EnterAsync applies the shared cooldown to this request and every other worker.
                    }
                    else if (retry.Transient)
                    {
                        var wait = retry.Delay ?? Backoff(transients + 1);
                        Pause(wait);
                        if (!allowRetries || transients++ >= transientRetries) return result;
                        retryDelay = wait;
                        if (result is IDisposable disposable) disposable.Dispose();
                        Serilog.Log.Warning("{Provider} catalog temporarily unavailable; retry {Retry} after {DelaySeconds:0.##} s",
                            provider, transients, retryDelay.Value.TotalSeconds);
                    }
                    else return result;
                }
            }
            // Do not occupy a request slot while waiting between transport retries.
            if (retryDelay is { } waitForRetry && waitForRetry > TimeSpan.Zero)
                await delay(waitForRetry, token).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(Exception error) => error is TimeoutException or IOException or OperationCanceledException
        || error is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout };

    private TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Min(30, baseDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt - 1, 10)));
        return TimeSpan.FromSeconds(seconds * (1 + Math.Clamp(jitter(), 0, 1) * 0.25));
    }

    private void Pause(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        lock (gate)
        {
            var now = utcNow();
            var until = duration >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + duration;
            if (until > cooldown) cooldown = until;
        }
    }

    private async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        while (true)
        {
            await slots.WaitAsync(token).ConfigureAwait(false);
            TimeSpan wait;
            try
            {
                lock (gate)
                {
                    var now = utcNow();
                    var earliest = nextStart > cooldown ? nextStart : cooldown;
                    wait = earliest - now;
                    if (wait <= TimeSpan.Zero)
                    {
                        nextStart = now + interval;
                        return new Lease(slots);
                    }
                }
            }
            catch { slots.Release(); throw; }
            slots.Release();
            // Long Retry-After values remain cancellable and can be extended by another in-flight response.
            await delay(wait > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : wait, token).ConfigureAwait(false);
        }
    }

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private SemaphoreSlim? held = slots;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
