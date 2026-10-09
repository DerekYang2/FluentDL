using FluentDL.Core.Sources;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Helpers;

public static class StatusLevelExtensions
{
    public static InfoBarSeverity ToSeverity(this StatusLevel level) => level switch
    {
        StatusLevel.Informational => InfoBarSeverity.Informational,
        StatusLevel.Success => InfoBarSeverity.Success,
        StatusLevel.Warning => InfoBarSeverity.Warning,
        StatusLevel.Error => InfoBarSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
