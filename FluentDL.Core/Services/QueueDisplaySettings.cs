using FluentDL.Core.Contracts.Services;

namespace FluentDL.Core.Services;

public sealed class QueueDisplaySettings(ILocalSettingsService settings) : CompactListSettings(settings, SettingsKey)
{
    public const string SettingsKey = "queue_compact_layout";
}
