namespace FluentDL.Core.Sources;

public struct ProgressData
{
    // Negative IDs cannot collide with the timestamp IDs used by the existing source downloaders.
    private static long nextId;
    public static long NewId() => Interlocked.Decrement(ref nextId);

    public long uniqueId;
    public long? totalBytes;
    public long? currentBytes;
    // Segmented downloads know their position in the manifest before they know the full byte count.
    public double? fraction;
    public readonly double? percent
    {
        get
        {
            if (fraction != null) return fraction;
            if (totalBytes > 0 && currentBytes != null)
            {
                return (double)currentBytes / totalBytes;
            }
            return null;
        }
    }
    public double? bps;

    public readonly double PercentRounded()
    {
        if (percent == null)
            return 0;
        return Math.Round((double)percent * 100d);
    }

    public readonly double TotalMB()
    {
        if (totalBytes == null)
            return 0;
        return Math.Round((double)totalBytes / 1024d / 1024d, 1);
    }
    public readonly double CurrentMB()
    {
        if (currentBytes == null)
            return 0;
        return Math.Round((double)currentBytes / 1024d / 1024d, 1);
    }
    public readonly double MBPS()
    {
        if (bps == null)
            return 0;
        return Math.Round((double)bps / 1024d / 1024d, 1);
    }
}
