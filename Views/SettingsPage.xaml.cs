using System.Diagnostics;
using FluentDL.Contracts.Services;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentDL.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private DispatcherQueue dispatcher;
    private ILocalSettingsService localSettings;


    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        localSettings = App.GetService<ILocalSettingsService>();
        this.Loaded += SettingsPage_Loaded;
        dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Set Ids/Secrets
        ClientIdInput.Text = (await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId)) ?? "";
        ClientSecretInput.Password = (await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret)) ?? "";
        DeezerARLInput.Text = await localSettings.ReadSettingAsync<string>(SettingsViewModel.DeezerARL);

        /*
        Thread thread = new Thread(() =>
        {
            var ripConfigPath = TerminalSubprocess.GetRunCommandSync("rip config path", "c:\\");

            // Find first index of ' and second index of ' in the string
            var firstIndex = ripConfigPath.IndexOf('\'');
            var secondIndex = ripConfigPath.IndexOf('\'', firstIndex + 1);
            ripConfigPath = ripConfigPath.Substring(firstIndex + 1, secondIndex - firstIndex - 1);

            dispatcher.TryEnqueue(() =>
            {
                configTextBlock.Text = ripConfigPath;
            });


            // Open the toml text file at ripConfigPath and save contents to string
            var configFileStr = System.IO.File.ReadAllText(ripConfigPath);
            Debug.WriteLine("CONFIG FILE:" + configFileStr);

            dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                RichEditBox.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, configFileStr);
            });
        });
        thread.Start();
        */
    }

    private async void ClientIdInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        // Save client id
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientId, ClientIdInput.Text.Trim());
        // Recreate spotify api object
        await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));
    }

    private async void ClientSecretInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        // TODO: encryption?
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientSecret, ClientSecretInput.Password.Trim());
        await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));
    }

    private async void DeezerARLInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerARL, DeezerARLInput.Text.Trim());
        await DeezerApi.InitDeezerClient(DeezerARLInput.Text.Trim());
    }
}