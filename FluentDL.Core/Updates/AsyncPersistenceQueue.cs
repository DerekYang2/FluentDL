#nullable enable
using System.Threading.Channels;

namespace FluentDL.Core.Updates;

// Preparation starts at enqueue time. The consumer preserves enqueue order and completes barriers after actual writes.
public sealed class AsyncPersistenceQueue<T>
{
    private sealed record Entry(Task<T>? Prepared, TaskCompletionSource Completion);
    private readonly Channel<Entry> channel = Channel.CreateUnbounded<Entry>(new() { SingleReader = true });
    private readonly Func<T, Task> write;
    private readonly Action<Exception>? report;
    private readonly Task consumer;
    private readonly object sync = new();
    private bool stopping;
    private Exception? failure;

    public AsyncPersistenceQueue(Func<T, Task> write, Action<Exception>? report = null)
    {
        this.write = write;
        this.report = report;
        consumer = Task.Run(ConsumeAsync);
    }

    public Task EnqueueAsync(Func<Task<T>> prepare)
    {
        lock (sync)
        {
            if (stopping) return Task.FromException(new InvalidOperationException("Queue persistence is stopping."));
            Task<T> prepared;
            try { prepared = prepare(); }
            catch (Exception error) { prepared = Task.FromException<T>(error); }
            return Enqueue(prepared);
        }
    }

    public Task FlushAsync(CancellationToken token = default) => Enqueue(null).WaitAsync(token);
    public async Task CompleteAsync()
    {
        lock (sync) { stopping = true; channel.Writer.TryComplete(); }
        await consumer;
    }

    private Task Enqueue(Task<T>? prepared)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!channel.Writer.TryWrite(new(prepared, completion)))
            completion.SetException(new InvalidOperationException("Queue persistence is stopping."));
        return completion.Task;
    }

    private async Task ConsumeAsync()
    {
        await foreach (var entry in channel.Reader.ReadAllAsync())
        {
            try
            {
                if (entry.Prepared != null) await write(await entry.Prepared);
                else if (failure != null) throw new IOException("A queue save failed. Restart and verify the queue before updating.", failure);
                entry.Completion.SetResult();
            }
            catch (Exception error)
            {
                failure ??= error;
                entry.Completion.SetException(error);
                try { report?.Invoke(error); } catch { }
            }
        }
    }
}
