using FluentDL.Core.Contracts.Services;

namespace FluentDL.Core.Services;

public sealed class LocalDisplaySettings(ILocalSettingsService settings) : CompactListSettings(settings, SettingsKey)
{
    public const string SettingsKey = "local_compact_layout";
}
