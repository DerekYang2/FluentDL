#nullable enable
using System.ComponentModel;
using FluentDL.Core.Contracts.Services;

namespace FluentDL.Core.Services;

public abstract class CompactListSettings : INotifyPropertyChanged
{
    private readonly ILocalSettingsService settings;
    private readonly string key;
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private bool compact = true;
    private bool updating;

    protected CompactListSettings(ILocalSettingsService settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.settings = settings;
        this.key = key;
    }

    public bool IsCompact => compact;
    public bool CanChangeLayout => !updating;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync()
    {
        await updateLock.WaitAsync();
        try { Apply(await settings.ReadSettingAsync<bool?>(key) ?? true); }
        finally { updateLock.Release(); }
    }

    public async Task SetCompactAsync(bool enabled)
    {
        await updateLock.WaitAsync();
        SetUpdating(true);
        try
        {
            await settings.SaveSettingAsync(key, enabled);
            Apply(enabled);
        }
        finally
        {
            SetUpdating(false);
            updateLock.Release();
        }
    }

    private void Apply(bool enabled)
    {
        if (compact == enabled) return;
        compact = enabled;
        PropertyChanged?.Invoke(this, new(nameof(IsCompact)));
    }

    private void SetUpdating(bool enabled)
    {
        updating = enabled;
        PropertyChanged?.Invoke(this, new(nameof(CanChangeLayout)));
    }
}
