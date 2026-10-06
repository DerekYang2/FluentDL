#nullable enable
using System.Runtime.InteropServices;
using R128Net;

namespace FluentDL.Core.ReplayGain;

// Measures one track's integrated loudness (ITU-R BS.1770) and sample peak from interleaved 32-bit float audio.
public sealed class LoudnessMeasurement : IDisposable
{
    private readonly LoudnessMeter meter;

    // channelMask is a WAV speaker mask, with one bit per channel in channel order. Without one, R128Net's default
    // layout applies: left, right, center, unused, left surround, right surround.
    public LoudnessMeasurement(int channels, int sampleRate, uint channelMask = 0)
    {
        meter = new LoudnessMeter(channels, sampleRate, LoudnessModes.Integrated | LoudnessModes.SamplePeak);
        var channel = 0;
        for (var bit = 0; bit < 32 && channel < channels; bit++)
        {
            var speaker = 1u << bit;
            if ((channelMask & speaker) != 0) meter.SetChannel(channel++, Position(speaker));
        }
    }

    // BS.1770 weights the LFE channel 0, the side and rear surrounds 1.41, and every other speaker 1.
    private static ChannelPosition Position(uint speaker) => speaker switch
    {
        0x1 => ChannelPosition.Left,
        0x2 => ChannelPosition.Right,
        0x8 => ChannelPosition.Unused,
        0x10 => ChannelPosition.LeftSurround,
        0x20 => ChannelPosition.RightSurround,
        0x200 => ChannelPosition.Mp090,
        0x400 => ChannelPosition.Mm090,
        _ => ChannelPosition.Center,
    };

    public double Loudness => meter.IntegratedLoudness;

    public double Peak
    {
        get
        {
            var peak = 0.0;
            for (var channel = 0; channel < meter.Channels; channel++) peak = Math.Max(peak, meter.GetSamplePeak(channel));
            return peak;
        }
    }

    public void AddFrames(ReadOnlySpan<float> interleaved) => meter.AddFrames(interleaved);

    // Reads interleaved 32-bit float samples until the stream ends. It blocks, so the scanner runs it on its own thread.
    public void Read(Stream pcm)
    {
        var frameBytes = meter.Channels * sizeof(float);
        var buffer = new byte[frameBytes * 16384];
        var filled = 0;
        int read;
        while ((read = pcm.Read(buffer, filled, buffer.Length - filled)) > 0)
        {
            filled += read;
            // Reads can end partway through a frame; keep the remainder for the next read.
            var whole = filled - filled % frameBytes;
            if (whole == 0) continue;
            AddFrames(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, whole)));
            Buffer.BlockCopy(buffer, whole, buffer, 0, filled - whole);
            filled -= whole;
        }
    }

    // The loudness of several tracks measured as one programme, which is how album gain is defined.
    // Averaging the tracks' loudness values gives a different answer.
    public static double CombinedLoudness(IEnumerable<LoudnessMeasurement> measurements)
    {
        var meters = measurements.Where(measurement => double.IsFinite(measurement.Loudness)).Select(measurement => measurement.meter).ToArray();
        return meters.Length == 0 ? double.NegativeInfinity : LoudnessMeter.GatedLoudness(meters);
    }

    public void Dispose() => meter.Dispose();
}
