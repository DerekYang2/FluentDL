using FluentDL.Core.ReplayGain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class LoudnessMeasurementTests
{
    private const int SampleRate = 48000;

    // EBU Tech 3341 test signal: a 1 kHz stereo sine at the given level in dBFS.
    private static float[] StereoSine(double dbfs, double seconds)
    {
        var amplitude = Math.Pow(10, dbfs / 20);
        var frames = (int)(SampleRate * seconds);
        var samples = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * frame / SampleRate));
            samples[2 * frame] = value;
            samples[2 * frame + 1] = value;
        }
        return samples;
    }

    [TestMethod]
    public void Loudness_MatchesTheEbuReferenceSignal()
    {
        // Arrange
        using var measurement = new LoudnessMeasurement(2, SampleRate);

        // Act
        measurement.AddFrames(StereoSine(-23, 20));

        // Assert: EBU Tech 3341 expects -23.0 LUFS within 0.1 LU.
        Assert.AreEqual(-23.0, measurement.Loudness, 0.1);
        Assert.AreEqual(Math.Pow(10, -23.0 / 20), measurement.Peak, 1e-3);
    }

    [TestMethod]
    public void Loudness_IsNegativeInfinityForSilence()
    {
        // Arrange
        using var measurement = new LoudnessMeasurement(2, SampleRate);

        // Act
        measurement.AddFrames(new float[SampleRate * 2 * 5]);

        // Assert
        Assert.IsTrue(double.IsNegativeInfinity(measurement.Loudness));
        Assert.AreEqual(0.0, measurement.Peak);
    }

    [TestMethod]
    public void Read_HandlesReadsThatSplitFrames()
    {
        // Arrange
        var samples = StereoSine(-20, 5);
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var whole = new LoudnessMeasurement(2, SampleRate);
        using var streamed = new LoudnessMeasurement(2, SampleRate);
        whole.AddFrames(samples);

        // Act
        streamed.Read(new ChunkedStream(bytes, chunkSize: 1237));

        // Assert
        Assert.AreEqual(whole.Loudness, streamed.Loudness, 1e-12);
        Assert.AreEqual(whole.Peak, streamed.Peak, 1e-12);
    }

    [TestMethod]
    public void CombinedLoudness_MeasuresTracksAsOneProgramme()
    {
        // Arrange: equal-length tracks at -23 and -13 LUFS average to -18 LUFS, but their energy combines
        // to 10 * log10((10^-2.3 + 10^-1.3) / 2), about -15.6 LUFS.
        using var quiet = new LoudnessMeasurement(2, SampleRate);
        using var loud = new LoudnessMeasurement(2, SampleRate);
        quiet.AddFrames(StereoSine(-23, 10));
        loud.AddFrames(StereoSine(-13, 10));

        // Act
        var combined = LoudnessMeasurement.CombinedLoudness([quiet, loud]);

        // Assert
        Assert.AreEqual(10 * Math.Log10((Math.Pow(10, -2.3) + Math.Pow(10, -1.3)) / 2), combined, 0.1);
    }

    [TestMethod]
    public void Calculator_WritesAlbumValuesFromTheWholeAlbum()
    {
        // Arrange
        using var quiet = new LoudnessMeasurement(2, SampleRate);
        using var loud = new LoudnessMeasurement(2, SampleRate);
        quiet.AddFrames(StereoSine(-23, 10));
        loud.AddFrames(StereoSine(-13, 10));
        var album = new List<(string, LoudnessMeasurement)> { ("quiet.flac", quiet), ("loud.flac", loud) };

        // Act
        var results = ReplayGainCalculator.Calculate([album], new ReplayGainOptions(Offset: -1));

        // Assert
        Assert.AreEqual(4.0, results[0].TrackGain, 0.1);
        Assert.AreEqual(-6.0, results[1].TrackGain, 0.1);
        Assert.AreEqual(-18 + 15.6 - 1, results[0].AlbumGain!.Value, 0.1);
        Assert.AreEqual(results[0].AlbumGain, results[1].AlbumGain);
        Assert.AreEqual(loud.Peak, results[0].AlbumPeak!.Value, 1e-12);
        Assert.IsFalse(results.Any(result => result.TrackClips || result.AlbumClips));
    }

    [TestMethod]
    public void Calculator_LowersClippingGainsOnlyWhenAsked()
    {
        // Arrange: a -20 dBFS sine peaks at 0.1 and measures -20 LUFS, so it needs +2 dB. A +20 dB offset makes
        // that +22 dB, which pushes the peak past full scale. Protection lowers it to the 0.01 dB step at or below
        // full scale. As a float the peak is 0.100000001, a hair over 0.1, so that's +19.99 dB rather than +20.00 dB.
        using var measurement = new LoudnessMeasurement(2, SampleRate);
        measurement.AddFrames(StereoSine(-20, 10));
        var album = new List<(string, LoudnessMeasurement)> { ("track.flac", measurement) };

        // Act
        var plain = ReplayGainCalculator.Calculate([album], new ReplayGainOptions(Offset: 20))[0];
        var protectedResult = ReplayGainCalculator.Calculate([album], new ReplayGainOptions(Offset: 20, PreventClipping: true))[0];

        // Assert
        Assert.IsTrue(plain.TrackClips);
        Assert.IsFalse(plain.TrackAdjusted);
        Assert.IsFalse(protectedResult.TrackClips);
        Assert.IsTrue(protectedResult.TrackAdjusted);
        Assert.AreEqual(19.99, protectedResult.TrackGain, 1e-9);
    }

    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(Math.Min(count, chunkSize), data.Length - position);
            Array.Copy(data, position, buffer, offset, read);
            position += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
