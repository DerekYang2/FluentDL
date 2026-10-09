namespace FluentDL.Core.Sources;

public struct ProgressData
{
    public long uniqueId;
    public long? totalBytes;
    public long? currentBytes;
    public readonly double? percent
    {
        get
        {
            if (totalBytes != null && currentBytes != null)
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
