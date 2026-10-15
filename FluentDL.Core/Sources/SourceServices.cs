using FluentDL.Core.Contracts.Services;

namespace FluentDL.Core.Sources;

/// <summary>
/// The app services the static source classes use, set once at startup. Temporary: the sources take these through
/// their constructors once they are instances (Phase 7 of the source refactor).
/// </summary>
public static class SourceServices
{
    private static ILocalSettingsService? settings;
    private static IAudioConverter? audioConverter;

    public static ILocalSettingsService Settings =>
        settings ?? throw new InvalidOperationException("SourceServices.Configure has not been called.");

    public static IAudioConverter AudioConverter =>
        audioConverter ?? throw new InvalidOperationException("SourceServices.Configure has not been called.");

    public static void Configure(ILocalSettingsService settingsService, IAudioConverter converter)
    {
        settings = settingsService;
        audioConverter = converter;
    }
}
