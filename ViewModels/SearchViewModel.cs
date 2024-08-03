using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;

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

    public SearchViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
        InitializeAsync(); // Initialize properties that require async calls
    }

    private async Task InitializeAsync()
    {
        ResultsLimit = await localSettings.ReadSettingAsync<int?>(ResultsLimitKey) ?? 25;
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