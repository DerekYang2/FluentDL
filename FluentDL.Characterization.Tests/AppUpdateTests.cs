using FluentDL.Contracts.Services;
using FluentDL.Core.Updates;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class AppUpdateTests
{
    [TestMethod]
    public void NormalBuildNeverIncludesTheSyntheticUpdaterBackend() =>
        Assert.IsNull(typeof(AppUpdateViewModel).Assembly.GetType("FluentDL.UpdaterTestSupport"));
    [TestMethod]
    public async Task CheckingAndDownloadingNeverStartsInstallation()
    {
        var backend = new FakeUpdates();
        var model = new AppUpdateViewModel(backend);
        await model.CheckAsync();
        Assert.IsTrue(model.CanAct);
        Assert.AreEqual("Download update", model.ActionText);
        await model.InstallAsync(new(), _ => throw new AssertFailedException("Not downloaded."));
        await model.DownloadAsync();
        Assert.IsTrue(model.IsDownloaded);
        Assert.AreEqual("Install update", model.ActionText);
        Assert.AreEqual(0, backend.Installs);
    }

    [TestMethod]
    public async Task BusyWorkPreventsPreparationAndInstallation()
    {
        var backend = new FakeUpdates();
        var model = await Downloaded(backend);
        var gate = new OperationGate();
        using var work = gate.EnterWork();
        await model.InstallAsync(gate, _ => throw new AssertFailedException("Work is active."));
        Assert.AreEqual(0, backend.Installs);
        StringAssert.Contains(model.Message, "Finish active");
        Assert.IsTrue(model.IsDownloaded);
    }

    [TestMethod]
    public async Task InstallationReservesTheGateAndWaitsForPersistence()
    {
        var backend = new FakeUpdates();
        var model = await Downloaded(backend);
        var gate = new OperationGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var install = model.InstallAsync(gate, async _ => { entered.SetResult(); await persisted.Task; });
        await entered.Task;
        Assert.IsTrue(gate.IsInstalling);
        Assert.ThrowsException<InvalidOperationException>(() => gate.EnterWork());
        Assert.AreEqual(0, backend.Installs);
        persisted.SetResult();
        await install;
        Assert.AreEqual(1, backend.Installs);
        Assert.IsFalse(gate.IsInstalling);
    }

    [TestMethod]
    public async Task FailedPersistenceDoesNotReachTheInstallerAndReleasesTheGate()
    {
        var backend = new FakeUpdates();
        var model = await Downloaded(backend);
        var gate = new OperationGate();
        await model.InstallAsync(gate, _ => throw new IOException("Synthetic save failure"));
        Assert.AreEqual(0, backend.Installs);
        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(gate.IsInstalling);
        using var work = gate.EnterWork();
        StringAssert.Contains(model.Message, "Could not install");
    }

    [TestMethod]
    public async Task CanceledNativeInstallationDoesNotRetryAndKeepsTheAppUsable()
    {
        var backend = new FakeUpdates { Install = _ => throw new OperationCanceledException() };
        var model = await Downloaded(backend);
        var gate = new OperationGate();
        await model.InstallAsync(gate, _ => Task.CompletedTask);
        Assert.AreEqual(1, backend.Installs);
        Assert.IsFalse(gate.IsInstalling);
        Assert.IsTrue(model.CanCheck);
        StringAssert.Contains(model.Message, "canceled");
    }

    [TestMethod]
    public async Task UpToDateAndFailedChecksCannotOfferInstallation()
    {
        var backend = new FakeUpdates { Offer = null };
        var model = new AppUpdateViewModel(backend);
        await model.CheckAsync();
        Assert.IsFalse(model.CanAct);
        StringAssert.Contains(model.Message, "up to date");
        backend.Check = _ => throw new IOException("Synthetic check failure");
        await model.CheckAsync();
        Assert.IsFalse(model.CanAct);
        Assert.IsTrue(model.CanCheck);
        Assert.AreEqual(0, backend.Installs);
    }

    [TestMethod]
    public async Task CancelingDownloadDoesNotPublishAReadyUpdate()
    {
        var backend = new FakeUpdates();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Download = async token => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var model = new AppUpdateViewModel(backend);
        await model.CheckAsync();
        var downloading = model.DownloadAsync();
        await entered.Task;
        Assert.IsTrue(model.CanCancel);
        model.Cancel();
        await downloading;
        Assert.IsFalse(model.IsDownloaded);
        Assert.IsFalse(model.CanCancel);
        Assert.IsTrue(model.CanCheck);
        Assert.AreEqual(0, backend.Installs);
    }

    private static async Task<AppUpdateViewModel> Downloaded(FakeUpdates backend)
    {
        var model = new AppUpdateViewModel(backend);
        await model.CheckAsync();
        await model.DownloadAsync();
        return model;
    }

    private sealed class FakeUpdates : IAppUpdateService
    {
        internal AppUpdateOffer? Offer = new("Synthetic version 2");
        internal int Installs;
        internal Func<CancellationToken, Task<AppUpdateOffer?>>? Check;
        internal Func<CancellationToken, Task> Download = _ => Task.CompletedTask;
        internal Func<CancellationToken, Task> Install = _ => Task.CompletedTask;
        public string InstallationDescription => "Synthetic update source";
        public Uri HelpUri => new("https://example.test/");
        public string HelpLabel => "Open test source";
        public Task<AppUpdateOffer?> CheckAsync(CancellationToken token) => Check?.Invoke(token) ?? Task.FromResult(Offer);
        public Task DownloadAsync(AppUpdateOffer offer, IProgress<double> progress, CancellationToken token) => Download(token);
        public Task InstallAsync(AppUpdateOffer offer, CancellationToken token) { Installs++; return Install(token); }
    }
}
