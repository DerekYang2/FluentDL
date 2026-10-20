using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Helpers;

// The callback's track may be refreshed metadata or an equivalent from another provider.
// Keep the badge result on the operation, not on that track object's identity.
internal sealed class QueueConversionOutcome(ConversionUpdateCallback report)
{
    private int level = (int)StatusLevel.Error;

    internal StatusLevel Level => (StatusLevel)Volatile.Read(ref level);

    internal void Report(StatusLevel value, SongSearchObject track, string? location = null)
    {
        Interlocked.Exchange(ref level, (int)value);
        report(value, track, location);
    }
}
