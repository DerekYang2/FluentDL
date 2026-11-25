using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Helpers;

// The callback's track may be refreshed metadata or an equivalent from another provider.
// Keep the badge result on the operation, not on that track object's identity.
internal sealed class QueueConversionOutcome(ConversionUpdateCallback report)
{
    private int level = (int)StatusLevel.Error;
    private SongSearchObject? track;

    internal StatusLevel Level => (StatusLevel)Volatile.Read(ref level);

    /// <summary>The track last reported, which may differ from the queue row (refreshed YouTube metadata).</summary>
    internal SongSearchObject? Track => Volatile.Read(ref track);

    internal void Report(StatusLevel value, SongSearchObject track, string? location = null)
    {
        Interlocked.Exchange(ref level, (int)value);
        Volatile.Write(ref this.track, track);
        report(value, track, location);
    }
}
