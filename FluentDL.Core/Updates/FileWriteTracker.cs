#nullable enable
namespace FluentDL.Core.Updates;

/// <summary>Tracks non-cancellable file writers until they return, even if a caller stops waiting.</summary>
public sealed class FileWriteTracker(OperationGate gate)
{
    private readonly object sync = new();
    private readonly HashSet<Task> pending = [];
    private bool closing;

    public Task RunAsync(Action write)
    {
        lock (sync)
        {
            if (closing) throw new InvalidOperationException("The app is closing. No new file writes can start.");
            var lease = gate.EnterWork();
            var task = Task.Run(() => { using (lease) write(); });
            pending.Add(task);
            _ = ObserveAsync(task);
            return task;
        }
    }

    public Task StopAndDrainAsync()
    {
        lock (sync)
        {
            closing = true;
            return Task.WhenAll(pending.ToArray());
        }
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* The returned task reports the error to the writer's caller. */ }
        finally { lock (sync) pending.Remove(task); }
    }
}
