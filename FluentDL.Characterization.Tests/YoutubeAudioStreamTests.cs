using FluentDL.Services.Sources;
using YoutubeExplode.Videos.Streams;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class YoutubeAudioStreamTests
{
    [TestMethod]
    public void PrefersAudioOnlyAndSelectsTheRequestedCodecAndBitrate()
    {
        var lowOpus = new AudioOnlyStreamInfo("low", Container.WebM, new(1), new(64_000), "opus", null, null);
        var highOpus = new AudioOnlyStreamInfo("high", Container.WebM, new(1), new(128_000), "opus", null, null);
        var aac = new AudioOnlyStreamInfo("aac", Container.Mp4, new(1), new(160_000), "mp4a.40.2", null, null);
        var manifest = new StreamManifest([lowOpus, highOpus, aac, Muxed(256_000)]);

        Assert.AreSame(highOpus, YoutubeSource.SelectAudioStream(manifest));
        Assert.AreSame(lowOpus, YoutubeSource.SelectAudioStream(manifest, worst: true));
        Assert.AreSame(aac, YoutubeSource.SelectAudioStream(manifest, preferAac: true));
        Assert.AreSame(aac, YoutubeSource.SelectAudioStream(new StreamManifest([aac, Muxed(256_000)])));
    }

    [TestMethod]
    public void FallsBackToTheHighestBitrateMuxedStream()
    {
        var best = Muxed(161_000);
        var manifest = new StreamManifest([Muxed(128_000), best]);

        Assert.AreSame(best, YoutubeSource.SelectAudioStream(manifest));
        Assert.AreSame(best, YoutubeSource.SelectAudioStream(manifest, preferAac: true));
        Assert.AreSame(best, YoutubeSource.SelectAudioStream(manifest, worst: true));
    }

    [TestMethod]
    public void EmptyManifestReportsNoDownloadableAudio()
    {
        var error = Assert.ThrowsException<InvalidOperationException>(() => YoutubeSource.SelectAudioStream(new StreamManifest([])));
        Assert.AreEqual("This YouTube video has no downloadable audio.", error.Message);
    }

    private static MuxedStreamInfo Muxed(long bitrate) =>
        new("muxed", Container.Mp4, new(1), new(bitrate), "mp4a.40.2", null, null, "avc1", new(360, 30), new(640, 360), false);
}
