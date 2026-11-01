using FluentDL.Helpers;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class UiOperationTests
{
    [TestMethod]
    public void NormalBuildExcludesTheHardeningProviderFixture() =>
        Assert.IsNull(typeof(UiOperation).Assembly.GetType("FluentDL.HardeningTestSupport"));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProviderFailuresBeforeAndAfterAnAwaitReachTheErrorHandler(bool afterAwait)
    {
        var error = new HttpRequestException("Synthetic provider failure");
        Exception? reported = null;
        Task BeforeAwait() => throw error;
        async Task AfterAwait() { await Task.Yield(); throw error; }
        var succeeded = await UiOperation.RunAsync(afterAwait ? AfterAwait : BeforeAwait, failure => reported = failure);
        Assert.IsFalse(succeeded);
        Assert.AreSame(error, reported);
    }

    [TestMethod]
    public async Task UserCancellationDoesNotShowAnErrorButAProviderTimeoutDoes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var errors = new List<Exception>();
        Assert.IsFalse(await UiOperation.RunAsync(() => Task.FromCanceled(cancellation.Token), errors.Add, cancellation.Token));
        Assert.AreEqual(0, errors.Count);
        var timeout = new TaskCanceledException("Synthetic provider timeout");
        Assert.IsFalse(await UiOperation.RunAsync(() => Task.FromException(timeout), errors.Add));
        Assert.AreSame(timeout, errors.Single());
    }

    [TestMethod]
    public async Task FailedDownloadStopsItsProgressLoopBeforeReportingTheError()
    {
        DownloadProgressHelper? progress = null;
        await UiOperation.RunAsync(async () =>
        {
            using var helper = progress = new DownloadProgressHelper("Synthetic download");
            helper.StartLoop();
            await Task.Yield();
            throw new HttpRequestException("Synthetic download failure");
        }, _ => Assert.IsFalse(progress!.IsRunning));
        Assert.IsFalse(progress!.IsRunning);
    }
}
