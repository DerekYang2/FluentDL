using FluentDL.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class FileWriteTrackerTests
{
    [TestMethod]
    public async Task ShutdownWaitsForTheWriterAfterItsCallerCancelsAndRejectsNewWriters()
    {
        var gate = new OperationGate();
        var tracker = new FileWriteTracker(gate);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrote = false;
        var writer = tracker.RunAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The synthetic writer was not released.");
            wrote = true;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var wait = writer.WaitAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => wait);
            var close = tracker.StopAndDrainAsync();
            Assert.IsFalse(close.IsCompleted);
            Assert.IsFalse(wrote);
            Assert.IsNull(gate.TryEnterInstallation());
            Assert.ThrowsException<InvalidOperationException>(() => tracker.RunAsync(() => Assert.Fail("Shutdown admitted a new writer.")));
            release.Set();
            await close;
            Assert.IsTrue(wrote);
            using var install = gate.TryEnterInstallation();
            Assert.IsNotNull(install);
        }
        finally { release.Set(); await writer; }
    }
}
