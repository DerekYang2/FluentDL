using Microsoft.UI.Xaml;

namespace FluentDL.Helpers;

/// <summary>
/// Keeps a centered InfoBar from twitching while it shows changing progress text ("99.5%" to "100.0%", an ETA that
/// comes and goes). During a run the content may grow but never shrink; the next ordinary message resets it.
/// </summary>
internal static class InfoBarWidth
{
    public static void Update(FrameworkElement content, bool hold) =>
        content.MinWidth = hold ? Math.Max(content.MinWidth, content.ActualWidth) : 0;
}
