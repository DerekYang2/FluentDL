using FluentDL.Contracts.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Helpers;
using FluentDL.Models;

using Microsoft.Extensions.Options;

using Windows.ApplicationModel;
using Windows.Storage;

namespace FluentDL.Services;

public class LocalSettingsService : ILocalSettingsService
{
    private const string _defaultApplicationDataFolder = "FluentDL/ApplicationData";
    private const string _defaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;

    private readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _applicationDataFolder;
    private readonly string _localsettingsFile;

    private IDictionary<string, object> _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options)
    {
        _fileService = fileService;
        _options = options.Value;

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? _defaultApplicationDataFolder);
        _localsettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, object>();
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, _localsettingsFile)) ?? new Dictionary<string, object>();

            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        try
        {
            if (RuntimeHelper.IsMSIX)
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj))
                {
                    return await Json.ToObjectAsync<T>((string)obj);
                }
            }
            else
            {
                await InitializeAsync();

                if (_settings != null && _settings.TryGetValue(key, out var obj))
                {
                    return await Json.ToObjectAsync<T>((string)obj);
                }
            }
        }
        catch (Exception)  // ToObjectAsync might fail on corrupted/incorrect data from import
        {
            if (RuntimeHelper.IsMSIX)
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey(key))
                {
                    ApplicationData.Current.LocalSettings.Values[key] = null;  // Set back to null so it can be reset on next open
                }
            }
            return default;
        }

        return default;
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
        if (RuntimeHelper.IsMSIX)
        {
            ApplicationData.Current.LocalSettings.Values[key] = await Json.StringifyAsync(value);
        }
        else
        {
            await InitializeAsync();

            _settings[key] = await Json.StringifyAsync(value);

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }
    }

    public async Task<string> ExportSettingsAsync()
    {
        if (RuntimeHelper.IsMSIX)
        {
            // Get local settings container
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

            // Create a dictionary to hold key-value pairs
            Dictionary<string, object> settingsDict = [];

            foreach (var item in localSettings.Values)
            {
                settingsDict[item.Key] = item.Value;
            }

            // Serialize to JSON
            string json = await Json.StringifyAsync(settingsDict);
            return json;
        }
        else
        {
            await InitializeAsync();
            return await Json.StringifyAsync(_settings);
        }
    }

    public async Task<string?> ImportSettingsAsync(string json)
    {
        try
        {
            var importedSettings = await Json.ToObjectAsync<Dictionary<string, object>>(json);
            if (importedSettings == null)
            {
                return "Settings Empty";
            }

            if (RuntimeHelper.IsMSIX)
            {
                // Get local settings container
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                int added = 0;
                foreach (var item in importedSettings)
                {
                    if (localSettings.Values.ContainsKey(item.Key))
                    {
                        localSettings.Values[item.Key] = item.Value;
                        added++;
                    }
                }

                int missed = localSettings.Values.Count - added;
                if (missed > 0)
                    return $"{missed} values missing from import file";
            }
            else
            {
                await InitializeAsync();
                foreach (var item in importedSettings)
                {
                    _settings[item.Key] = item.Value;
                }
                await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
            }
            return null;
        } catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
