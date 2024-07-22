using System.Diagnostics;
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

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
        dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Thread thread = new Thread(() =>
        {
            var ripSubprocess = new TerminalSubprocess();
            var ripConfigPath = ripSubprocess.RunCommandSync("rip config path");
            ripSubprocess.Dispose();

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
    }

    private void SpotifyDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Save the user's input
        var clientId = ClientIdInput.Text;
        var clientSecret = ClientSecretInput.Text;

        var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
        localSettings.Values["SpotifyClientId"] = clientId;
        localSettings.Values["SpotifyClientSecret"] = clientSecret;

        SpotifyApi.Initialize().Wait();
    }

    private async void OpenSpotifyDialog_OnClick(object sender, RoutedEventArgs e)
    {
        SpotifyDialog.XamlRoot = this.XamlRoot;
        var result = await SpotifyDialog.ShowAsync();
    }
}