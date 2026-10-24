#nullable enable
using FluentDL.Core.Updates;

namespace FluentDL.Core.Services;

public sealed record LocalConversionItemResult<T>(int Index, T Item, string? OutputPath, Exception? Error)
{
    public bool Succeeded => Error == null && !string.IsNullOrWhiteSpace(OutputPath);
}

public sealed record LocalConversionProgress<T>(int Completed, int Total, LocalConversionItemResult<T> Result);

public sealed record LocalConversionBatchResult<T>(IReadOnlyList<LocalConversionItemResult<T>> Results, bool Cancelled);

public sealed class LocalConversionBatch<T>
{
    private readonly OperationGate gate;
    private int running;

    public LocalConversionBatch(OperationGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        this.gate = gate;
    }

    public bool IsRunning => Volatile.Read(ref running) != 0;

    public async Task<LocalConversionBatchResult<T>> RunAsync(IEnumerable<T> items, int workerCount,
        Func<T, Task<string?>> convert, IProgress<LocalConversionProgress<T>>? progress = null,
        CancellationToken token = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("The previous conversion batch is still running.");
        try
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(convert);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
            using var work = gate.EnterWork();
            var snapshot = items.ToArray();
            var results = new List<LocalConversionItemResult<T>>();
            var completedSync = new object();
            var next = -1;

            async Task WorkerAsync()
            {
                while (!token.IsCancellationRequested)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= snapshot.Length || token.IsCancellationRequested) return;
                    string? path = null;
                    Exception? error = null;
                    // Cancel stops new files. An encoder already writing must return before this batch ends.
                    try { path = await convert(snapshot[index]).ConfigureAwait(false); }
                    catch (Exception failure) { error = failure; }
                    var result = new LocalConversionItemResult<T>(index, snapshot[index], path, error);
                    lock (completedSync)
                    {
                        results.Add(result);
                        progress?.Report(new(results.Count, snapshot.Length, result));
                    }
                }
            }

            var workers = Enumerable.Range(0, Math.Min(workerCount, snapshot.Length))
                .Select(_ => Task.Factory.StartNew(WorkerAsync, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();
            // Keep the busy state and update-installation lease until every encoder has returned.
            await Task.WhenAll(workers).ConfigureAwait(false);
            return new(results.ToArray(), token.IsCancellationRequested);
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
