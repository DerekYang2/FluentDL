using System.ComponentModel;
using FluentDL.Contracts.Services;

namespace FluentDL.Services;

public sealed class QueueDisplaySettings : INotifyPropertyChanged
{
    public const string SettingsKey = "queue_compact_layout";

    private readonly ILocalSettingsService _settings;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private bool _isCompact;
    private bool _isUpdating;

    public bool IsCompact => _isCompact;
    public bool CanChangeLayout => !_isUpdating;

    public event PropertyChangedEventHandler? PropertyChanged;

    public QueueDisplaySettings(ILocalSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public async Task LoadAsync()
    {
        await _updateLock.WaitAsync();
        SetUpdating(true);
        try
        {
            var saved = await _settings.ReadSettingAsync<bool?>(SettingsKey);
            if (saved is null)
            {
                await _settings.SaveSettingAsync(SettingsKey, false);
            }
            Apply(saved ?? false);
        }
        finally
        {
            SetUpdating(false);
            _updateLock.Release();
        }
    }

    public async Task SetCompactAsync(bool enabled)
    {
        await _updateLock.WaitAsync();
        SetUpdating(true);
        try
        {
            await _settings.SaveSettingAsync(SettingsKey, enabled);
            Apply(enabled);
        }
        finally
        {
            SetUpdating(false);
            _updateLock.Release();
        }
    }

    private void Apply(bool enabled)
    {
        if (_isCompact == enabled) return;
        _isCompact = enabled;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompact)));
    }

    private void SetUpdating(bool updating)
    {
        _isUpdating = updating;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanChangeLayout)));
    }
}
