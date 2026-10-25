using FluentDL.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueConversionBatchTests
{
    [TestMethod]
    public async Task StoppingAHugeQueueCancelsOnlyActiveWorkersAndStartsNoWaitingRows()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cancelledCalls = 0;
        var run = QueueConversionBatch.RunAsync(Enumerable.Range(0, 100_000).ToArray(), 4, async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 4) started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelledCalls); throw; }
        }, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(4, calls);
        Assert.AreEqual(4, cancelledCalls, "Waiting rows must not each create a task or cancellation exception.");
    }

    [TestMethod]
    public async Task StopWaitsForUncancellableActiveWorkWithoutStartingMoreRows()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var completed = 0;
        var run = QueueConversionBatch.RunAsync(Enumerable.Range(0, 50_000).ToArray(), 2, async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2) started.TrySetResult();
            await finish.Task;
            Interlocked.Increment(ref completed);
        }, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.IsFalse(run.IsCompleted);
        finish.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, calls);
        Assert.AreEqual(2, completed);
    }

    [TestMethod]
    public async Task EveryRowRunsOnceAndConcurrencyNeverExceedsTheWorkerCount()
    {
        var calls = new int[103];
        var active = 0;
        var peak = 0;
        await QueueConversionBatch.RunAsync(Enumerable.Range(0, calls.Length).ToArray(), 3, async (index, _) =>
        {
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = peak; if (previous >= current) break; } while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
            Interlocked.Increment(ref calls[index]);
            await Task.Yield();
            Interlocked.Decrement(ref active);
        });
        Assert.IsTrue(calls.All(count => count == 1));
        Assert.IsTrue(peak <= 3);
        Assert.AreEqual(0, active);
    }

    [TestMethod]
    public async Task PreCancelledAndEmptyRunsStartNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await QueueConversionBatch.RunAsync(new[] { 1, 2 }, 4, (_, _) => throw new AssertFailedException("Cancelled work started."), cancellation.Token);
        await QueueConversionBatch.RunAsync(Array.Empty<int>(), 4, (_, _) => throw new AssertFailedException("Empty work started."));
    }

    [TestMethod]
    public async Task CancellationDoesNotHideUnrelatedProviderErrors()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => QueueConversionBatch.RunAsync(new[] { 1 }, 1,
            (_, _) => throw new InvalidOperationException("Provider failed.")));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => QueueConversionBatch.RunAsync(new[] { 1 }, 1,
            (_, _) => throw new OperationCanceledException("An unrelated operation cancelled.")));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => QueueConversionBatch.RunAsync(new[] { 1 }, 0, (_, _) => Task.CompletedTask));
    }

    [TestMethod]
    public async Task QueueEditsCannotChangeTheRunningInputSnapshot()
    {
        var source = new List<int> { 1, 2, 3 };
        var visited = new List<int>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = QueueConversionBatch.RunAsync(source, 1, async (item, _) =>
        {
            visited.Add(item);
            if (item == 1) { started.TrySetResult(); await finish.Task; }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Clear();
        source.Add(99);
        finish.TrySetResult();
        await run;
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, visited);
    }
}
