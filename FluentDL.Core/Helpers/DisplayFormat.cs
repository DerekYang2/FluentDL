namespace FluentDL.Core.Helpers;

public static class DisplayFormat
{
    /// <summary>Shortens a count for display: 2.50B, 1.2M, 12K, or the number itself below a thousand.</summary>
    public static string FormatLargeValue(long value)
    {
        if (value >= 1e9) // Round two decimal places
        {
            return string.Format("{0:F2}B", value / 1e9);
        }

        if (value >= 1e6)
        {
            return string.Format("{0:F1}M", value / 1e6);
        }

        if (value >= 1e3)
        {
            return string.Format("{0:F0}K", value / 1e3);
        }

        return value.ToString();
    }
}
