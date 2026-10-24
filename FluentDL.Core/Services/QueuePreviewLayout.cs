#nullable enable
namespace FluentDL.Core.Services;

public readonly record struct QueuePreviewLayout(bool IsStacked, double AvailablePreviewWidth)
{
    public const double StackedBreakpoint = 840;
    private const double MinimumQueueWidth = 400;
    private const double PaneGap = 12;

    public static QueuePreviewLayout ForWidth(double width)
    {
        width = double.IsFinite(width) ? Math.Max(0, width) : 0;
        var stacked = width < StackedBreakpoint;
        return new(stacked, stacked ? width : width - MinimumQueueWidth - PaneGap);
    }
}
