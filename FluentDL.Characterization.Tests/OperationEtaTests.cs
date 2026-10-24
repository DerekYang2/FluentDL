using FluentDL.Helpers;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class OperationEtaTests
{
    [TestMethod]
    public void DiscoveryAndValidationTimeDoNotInflateTheEstimate()
    {
        var time = new ManualTime();
        var eta = new OperationEta(time);
        time.Advance(120);
        Assert.IsNull(eta.Update(0, 10));
        time.Advance(10);
        Assert.AreEqual("Estimated time: 1 min 30 sec", eta.Update(1, 10));
    }

    [TestMethod]
    public void EachOperationOrReplayGainPhaseGetsANewClockAndVisibilityState()
    {
        var time = new ManualTime();
        var eta = new OperationEta(time);
        eta.Update(0, 10);
        time.Advance(20);
        Assert.IsNotNull(eta.Update(2, 10));
        eta.Reset();
        time.Advance(60);
        Assert.IsNull(eta.Update(0, 10));
        time.Advance(2);
        Assert.IsNull(eta.Update(1, 10), "Tag writing must not reuse the analysis duration.");
    }

    [TestMethod]
    public void EstimatesUseTheActualWorkCountAfterSkippingTaggedTracks()
    {
        var time = new ManualTime();
        var eta = new OperationEta(time);
        eta.Update(0, 4);
        time.Advance(20);
        Assert.AreEqual("Estimated time: 1 min 0 sec", eta.Update(1, 4));
        Assert.IsNull(eta.Update(4, 4));
    }

    [TestMethod]
    public void EmptyAndShortWorkDoesNotShowAnEstimate()
    {
        var time = new ManualTime();
        var eta = new OperationEta(time);
        Assert.IsNull(eta.Update(0, 0));
        eta.Update(0, 2);
        time.Advance(3);
        Assert.IsNull(eta.Update(1, 2));
        Assert.IsNull(eta.Update(2, 2));
    }

    private sealed class ManualTime : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(double seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
