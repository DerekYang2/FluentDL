using FluentDL.Core.Services;
using FluentDL.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class LocalConversionBatchTests
{
    [TestMethod]
    public async Task CancelKeepsTheRunAndInstallationLeaseUntilStartedFilesFinish()
    {
        var gate = new OperationGate();
        var batch = new LocalConversionBatch<int>(gate);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var run = batch.RunAsync(new[] { 1, 2, 3 }, 1, async _ =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            return await finish.Task;
        }, token: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.IsTrue(batch.IsRunning);
        Assert.IsFalse(run.IsCompleted);
        Assert.AreEqual(1, gate.ActiveCount);
        Assert.IsNull(gate.TryEnterInstallation());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => batch.RunAsync(new[] { 99 }, 1, _ => Task.FromResult<string?>("unexpected")));
        Assert.IsTrue(batch.IsRunning, "A rejected restart must not release the first run's busy state.");
        finish.SetResult("finished.flac");
        var result = await run;

        Assert.IsTrue(result.Cancelled);
        Assert.AreEqual(1, calls);
        Assert.AreEqual("finished.flac", result.Results.Single().OutputPath);
        Assert.IsFalse(batch.IsRunning);
        Assert.AreEqual(0, gate.ActiveCount);
        using var installation = gate.TryEnterInstallation();
        Assert.IsNotNull(installation);
    }

    [TestMethod]
    public async Task ASourceListChangeDoesNotChangeTheRunningSnapshotOrTotals()
    {
        var source = new List<int> { 1, 2, 3 };
        var batch = new LocalConversionBatch<int>(new OperationGate());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new List<LocalConversionProgress<int>>();
        var progress = new InlineProgress<LocalConversionProgress<int>>(reports.Add);
        var run = batch.RunAsync(source, 1, async item =>
        {
            if (item == 1) { started.SetResult(); await finish.Task; }
            return item + ".flac";
        }, progress);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Clear();
        source.Add(99);
        finish.SetResult();
        var result = await run;

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Results.Select(item => item.Item).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, reports.Select(item => item.Completed).ToArray());
        Assert.IsTrue(reports.All(item => item.Total == 3));
        Assert.IsFalse(result.Cancelled);
    }

    [TestMethod]
    public async Task CancelDrainsEveryParallelWorkerAndStartsNoMoreFiles()
    {
        var batch = new LocalConversionBatch<int>(new OperationGate());
        using var cancellation = new CancellationTokenSource();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinish = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinish = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var run = batch.RunAsync(Enumerable.Range(0, 20), 2, item =>
        {
            if (Interlocked.Increment(ref calls) == 2) bothStarted.SetResult();
            return item == 0 ? firstFinish.Task : secondFinish.Task;
        }, token: cancellation.Token);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        firstFinish.SetResult("first.flac");
        Assert.IsFalse(run.IsCompleted, "The second encoder still owns an output file.");
        Assert.IsTrue(batch.IsRunning);
        secondFinish.SetResult("second.flac");
        var result = await run;
        Assert.AreEqual(2, calls);
        Assert.AreEqual(2, result.Results.Count);
        Assert.IsTrue(result.Results.All(item => item.Succeeded));
    }

    [TestMethod]
    public async Task ANewBatchCanStartAfterTheCancelledBatchHasDrained()
    {
        var batch = new LocalConversionBatch<int>(new OperationGate());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await batch.RunAsync(new[] { 1 }, 1, _ => throw new AssertFailedException("A canceled batch must not start a file."), token: cancellation.Token);
        Assert.IsTrue(cancelled.Cancelled);
        Assert.AreEqual(0, cancelled.Results.Count);

        var next = await batch.RunAsync(new[] { 2, 3 }, 2, item => Task.FromResult<string?>(item + ".flac"));
        Assert.IsFalse(next.Cancelled);
        CollectionAssert.AreEquivalent(new[] { 2, 3 }, next.Results.Select(item => item.Item).ToArray());
    }

    [TestMethod]
    public async Task FileFailuresAreReportedWithoutAbortingOtherWorkers()
    {
        var gate = new OperationGate();
        var batch = new LocalConversionBatch<int>(gate);
        var result = await batch.RunAsync(new[] { 0, 1, 2 }, 2, item => item switch
        {
            0 => throw new IOException("Synthetic encoder failure"),
            1 => Task.FromResult<string?>(null),
            _ => Task.FromResult<string?>("good.flac"),
        });
        Assert.AreEqual(3, result.Results.Count);
        Assert.AreEqual(1, result.Results.Count(item => item.Succeeded));
        Assert.IsInstanceOfType<IOException>(result.Results.Single(item => item.Item == 0).Error);
        Assert.IsFalse(batch.IsRunning);
        Assert.AreEqual(0, gate.ActiveCount);
    }

    [TestMethod]
    public async Task ActiveInstallationPreventsAnyEncoderFromStarting()
    {
        var gate = new OperationGate();
        using var installation = gate.TryEnterInstallation();
        var batch = new LocalConversionBatch<int>(gate);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => batch.RunAsync(new[] { 1 }, 1,
            _ => throw new AssertFailedException("The installer already reserved the application.")));
        Assert.IsFalse(batch.IsRunning);
        Assert.AreEqual(0, gate.ActiveCount);
    }

    [TestMethod]
    public async Task EmptyAndInvalidBatchesReleaseTheirBusyState()
    {
        var gate = new OperationGate();
        var batch = new LocalConversionBatch<int>(gate);
        var empty = await batch.RunAsync(Array.Empty<int>(), 3, _ => Task.FromResult<string?>("unexpected"));
        Assert.AreEqual(0, empty.Results.Count);
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => batch.RunAsync(new[] { 1 }, 0, _ => Task.FromResult<string?>(null)));
        Assert.IsFalse(batch.IsRunning);
        Assert.AreEqual(0, gate.ActiveCount);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
