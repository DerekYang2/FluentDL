using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;
using Microsoft.UI.Xaml;

namespace FluentDL.ViewModels;

public partial class SearchViewModel : ObservableRecipient
{
    private ILocalSettingsService localSettings;

    public int ResultsLimit
    {
        get;
        set;
    }

    public bool AlbumMode
    {
        get;
        set;
    }

    // Shortcut button visibilities
    public Visibility AddQueueVisibility
    {
        get;
        set;
    }

    public Visibility DownloadVisibility
    {
        get;
        set;
    }

    public Visibility ShareVisibility
    {
        get;
        set;
    }

    public Visibility OpenVisibility
    {
        get;
        set;
    }

    public SearchViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
    }

    public async Task InitializeAsync()
    {
        ResultsLimit = await localSettings.ReadSettingAsync<int?>(nameof(ResultsLimit)) ?? 25;
        AlbumMode = await localSettings.ReadSettingAsync<bool?>(nameof(AlbumMode)) ?? false;
        AddQueueVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchAddChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        DownloadVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchDownloadChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        ShareVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchShareChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        OpenVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchOpenChecked) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public async Task<bool> GetNotifyUpdate() {
        return await localSettings.ReadSettingAsync<bool>(SettingsViewModel.NotifyUpdate);
    }

    public async Task<string> GetSearchSource()
    {
        return await localSettings.ReadSettingAsync<string>(SettingsViewModel.SearchSource) ?? "deezer";
    }

    public async Task SaveSearchSource(string searchSource)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.SearchSource, searchSource);
    }

    public async Task SaveResultsLimit()
    {
        await localSettings.SaveSettingAsync(nameof(ResultsLimit), ResultsLimit);
    }

    public async Task SaveAlbumMode()
    {
        await localSettings.SaveSettingAsync(nameof(AlbumMode), AlbumMode);
    }
}