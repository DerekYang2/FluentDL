using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;

namespace FluentDL.ViewModels;

public partial class SearchViewModel : ObservableRecipient
{
    private ILocalSettingsService localSettings;

    public SearchViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
    }

    public async Task<string?> GetSearchSource()
    {
        return await localSettings.ReadSettingAsync<string>(SettingsViewModel.SearchSource);
    }

    public async Task SetSearchSource(string searchSource)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.SearchSource, searchSource);
    }
}