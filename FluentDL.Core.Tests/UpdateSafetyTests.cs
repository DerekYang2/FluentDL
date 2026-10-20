using FluentDL.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class UpdateSafetyTests
{
    [TestMethod]
    public void ExclusiveQueueChangesCannotOverlapWorkOrInstallation()
    {
        var gate = new OperationGate();
        using (var work = gate.EnterWork()) Assert.IsNull(gate.TryEnterExclusiveWork());
        using (var install = gate.TryEnterInstallation()) Assert.IsNull(gate.TryEnterExclusiveWork());
        var change = gate.TryEnterExclusiveWork();
        Assert.IsNotNull(change);
        Assert.IsTrue(gate.IsExclusiveWork);
        Assert.IsFalse(gate.IsInstalling);
        Assert.AreEqual(1, gate.ActiveCount);
        Assert.IsNull(gate.TryEnterInstallation());
        Assert.IsNull(gate.TryEnterExclusiveWork());
        Assert.ThrowsException<InvalidOperationException>(() => gate.EnterWork());
        change.Dispose();
        change.Dispose();
        Assert.IsFalse(gate.IsExclusiveWork);
        Assert.AreEqual(0, gate.ActiveCount);
        using var next = gate.EnterWork();
    }
    [TestMethod]
    public void ChannelSelectionNeverCrossesStoreAndSideloadIdentities()
    {
        var store = new UpdateInstallation(UpdateChannels.Name, UpdateChannels.StorePublisher, new(1, 0, 0, 0), "x64");
        var sideload = store with { Publisher = UpdateChannels.UnsignedPublisher };
        Assert.AreEqual(UpdateChannel.MicrosoftStore, UpdateChannels.Select(store, false, true, false, false));
        Assert.AreEqual(UpdateChannel.Github, UpdateChannels.Select(sideload, false, false, true, true));
        Assert.AreEqual(UpdateChannel.Unsupported, UpdateChannels.Select(store, true, true, false, true));
        Assert.AreEqual(UpdateChannel.Unsupported, UpdateChannels.Select(store, false, false, true, true));
        Assert.AreEqual(UpdateChannel.Unsupported, UpdateChannels.Select(sideload, false, false, true, false));
        Assert.AreEqual(UpdateChannel.Unsupported, UpdateChannels.Select(sideload with { Architecture = "x86" }, false, false, true, true));
        Assert.AreEqual(UpdateChannel.Unsupported, UpdateChannels.Select(store with { Name = "OtherApp" }, false, true, false, true));
    }
    [TestMethod]
    public void InstallationAndWorkCannotOverlapAndLeasesAreIdempotent()
    {
        var gate = new OperationGate();
        var work = gate.EnterWork();
        Assert.AreEqual(1, gate.ActiveCount);
        Assert.IsNull(gate.TryEnterInstallation());
        work.Dispose();
        work.Dispose();
        Assert.AreEqual(0, gate.ActiveCount);
        using var install = gate.TryEnterInstallation();
        Assert.IsNotNull(install);
        Assert.IsTrue(gate.IsInstalling);
        Assert.ThrowsException<InvalidOperationException>(() => gate.EnterWork());
        Assert.IsNull(gate.TryEnterInstallation());
        install.Dispose();
        install.Dispose();
        using var next = gate.EnterWork();
        Assert.AreEqual(1, gate.ActiveCount);
    }

    [TestMethod]
    public async Task FlushWaitsForPreparationAndActualWriteNotJustAnEmptyChannel()
    {
        var prepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new AsyncPersistenceQueue<int>(async value => { Assert.AreEqual(7, value); writing.SetResult(); await finish.Task; });
        var save = queue.EnqueueAsync(() => prepared.Task);
        var flush = queue.FlushAsync();
        Assert.IsFalse(flush.IsCompleted);
        prepared.SetResult(7);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(save.IsCompleted);
        Assert.IsFalse(flush.IsCompleted);
        finish.SetResult();
        await save;
        await flush;
        await queue.CompleteAsync();
    }

    [TestMethod]
    public async Task PersistenceKeepsEnqueueOrderWhenPreparationsFinishOutOfOrder()
    {
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new List<int>();
        var queue = new AsyncPersistenceQueue<int>(value => { written.Add(value); return Task.CompletedTask; });
        var a = queue.EnqueueAsync(() => first.Task);
        var b = queue.EnqueueAsync(() => Task.FromResult(2));
        Assert.IsFalse(b.IsCompleted);
        first.SetResult(1);
        await Task.WhenAll(a, b);
        await queue.FlushAsync();
        CollectionAssert.AreEqual(new[] { 1, 2 }, written);
        await queue.CompleteAsync();
    }

    [TestMethod]
    public async Task FailedSaveBlocksUpdateFlushAndDoesNotStopLaterQueueWrites()
    {
        var written = new List<int>();
        var queue = new AsyncPersistenceQueue<int>(value =>
        {
            if (value == 1) throw new IOException("Synthetic database failure");
            written.Add(value);
            return Task.CompletedTask;
        });
        await Assert.ThrowsExceptionAsync<IOException>(() => queue.EnqueueAsync(() => Task.FromResult(1)));
        await queue.EnqueueAsync(() => Task.FromResult(2));
        await Assert.ThrowsExceptionAsync<IOException>(() => queue.FlushAsync());
        CollectionAssert.AreEqual(new[] { 2 }, written);
        await queue.CompleteAsync();
    }

    [TestMethod]
    public async Task ClosingTheQueueWaitsForTheLastWriteToFinish()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new AsyncPersistenceQueue<int>(async _ => { entered.SetResult(); await finish.Task; });
        var save = queue.EnqueueAsync(() => Task.FromResult(1));
        await entered.Task;
        var closing = queue.CompleteAsync();
        Assert.IsFalse(closing.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(save, closing);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => queue.EnqueueAsync(() => Task.FromResult(2)));
    }
}
