using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;
using Microsoft.UI.Xaml;

namespace FluentDL.ViewModels;

public partial class SearchViewModel : ObservableRecipient
{
    private ILocalSettingsService localSettings;
    public static readonly string ResultsLimitKey = "ResultsLimit";

    public int ResultsLimit
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
        ResultsLimit = await localSettings.ReadSettingAsync<int?>(ResultsLimitKey) ?? 25;
        AddQueueVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchAddChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        ShareVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchShareChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        OpenVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.SearchOpenChecked) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public async Task<string?> GetSearchSource()
    {
        return await localSettings.ReadSettingAsync<string>(SettingsViewModel.SearchSource);
    }

    public async Task SetSearchSource(string searchSource)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.SearchSource, searchSource);
    }

    public async Task SaveResultsLimit()
    {
        await localSettings.SaveSettingAsync(ResultsLimitKey, ResultsLimit);
    }
}