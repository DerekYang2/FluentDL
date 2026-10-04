#nullable enable
namespace FluentDL.Core.ReplayGain;

public static class ReplayGainCalculator
{
    // Each inner list is one album. Album gain uses the album's tracks measured together, and the
    // album peak is the highest track peak.
    public static List<ReplayGainResult> Calculate(
        IEnumerable<IReadOnlyList<(string Path, LoudnessMeasurement Measurement)>> albums, ReplayGainOptions options)
    {
        var results = new List<ReplayGainResult>();
        foreach (var album in albums)
        {
            if (album.Count == 0) continue;

            double? albumGain = null, albumPeak = null;
            bool albumClips = false, albumAdjusted = false;
            if (options.AlbumGain)
            {
                var peak = album.Max(track => track.Measurement.Peak);
                var loudness = LoudnessMeasurement.CombinedLoudness(album.Select(track => track.Measurement));
                (var gain, albumAdjusted) = Apply(ReplayGainMath.Gain(loudness, options.Offset), peak, options.PreventClipping);
                albumGain = gain;
                albumPeak = peak;
                albumClips = ReplayGainMath.WouldClip(gain, peak);
            }

            foreach (var (path, measurement) in album)
            {
                var peak = measurement.Peak;
                var (trackGain, trackAdjusted) = Apply(ReplayGainMath.Gain(measurement.Loudness, options.Offset), peak, options.PreventClipping);
                results.Add(new ReplayGainResult(
                    path, measurement.Loudness, trackGain, peak, ReplayGainMath.WouldClip(trackGain, peak), trackAdjusted,
                    albumGain, albumPeak, albumClips, albumAdjusted));
            }
        }
        return results;
    }

    private static (double Gain, bool Adjusted) Apply(double gain, double peak, bool preventClipping)
    {
        if (!preventClipping) return (gain, false);
        var lowered = ReplayGainMath.PreventClipping(gain, peak);
        return (lowered, lowered < gain);
    }
}
