using FluentDL.Core.Contracts.Services;

namespace FluentDL.Core.Services;

public sealed class PreviewVolumeSettings
{
    public const string SettingsKey = "preview_player_volume";

    private readonly ILocalSettingsService settings;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly object sync = new();
    private double volume = 1;
    private long revision;

    public PreviewVolumeSettings(ILocalSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.settings = settings;
    }

    public double Volume { get { lock (sync) return volume; } }

    public async Task LoadAsync()
    {
        await saveLock.WaitAsync();
        try
        {
            long before;
            lock (sync) before = revision;
            var saved = await settings.ReadSettingAsync<double?>(SettingsKey);
            lock (sync)
            {
                // A volume change during startup or settings import takes precedence over the old saved value.
                if (revision == before)
                    volume = saved is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
            }
        }
        finally { saveLock.Release(); }
    }

    public async Task SetVolumeAsync(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(value));

        // Another page must see the new value even while the settings write is still pending.
        lock (sync)
        {
            volume = value;
            revision++;
        }
        await saveLock.WaitAsync();
        try
        {
            // Serialize writes and persist the latest value, not an older queued slider event.
            await settings.SaveSettingAsync(SettingsKey, Volume);
        }
        finally { saveLock.Release(); }
    }
}
