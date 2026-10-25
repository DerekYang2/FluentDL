namespace FluentDL.Core.Services;

public static class QueueConversionBatch
{
    public static async Task RunAsync<T>(IReadOnlyList<T> items, int workerCount,
        Func<T, CancellationToken, Task> process, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        var snapshot = items.ToArray();
        var next = -1;

        async Task WorkerAsync()
        {
            // Preserve the caller's context, but let every worker start before taking more rows.
            await Task.Yield();
            while (!token.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= snapshot.Length || token.IsCancellationRequested) return;
                try { await process(snapshot[index], token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                // A synchronously completed provider must not monopolize the UI thread.
                await Task.Yield();
            }
        }

        var workers = Enumerable.Range(0, Math.Min(workerCount, snapshot.Length)).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers);
    }
}
