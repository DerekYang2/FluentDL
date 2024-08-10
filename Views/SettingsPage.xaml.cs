using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using CommunityToolkit.WinUI;
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
        // Set download directory
        FolderTextBox.Text = await localSettings.ReadSettingAsync<string>(SettingsViewModel.DownloadDirectory) ?? "";

        // Set ask before download toggle
        AskToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AskBeforeDownload);

        // Set Ids/Secrets
        ClientIdInput.Text = (await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId)) ?? "";
        SpotifySecretInput.Password = (await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret)) ?? "";
        DeezerARLInput.Password = await localSettings.ReadSettingAsync<string>(SettingsViewModel.DeezerARL);
        QobuzIDInput.Text = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId);
        QobuzTokenInput.Password = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken);

        // Set lost input focus events
        DeezerARLInput.LostFocus += DeezerARLInput_OnLostFocus;
        SpotifySecretInput.LostFocus += SpotifySecretInput_OnLostFocus;
        QobuzTokenInput.LostFocus += QobuzTokenInput_OnLostFocus;

        // Set source combo box
        var searchSource = await localSettings.ReadSettingAsync<string>(SettingsViewModel.SearchSource);
        foreach (ComboBoxItem cbi in SearchSourceComboBox.Items)
        {
            if (cbi.Content as string == searchSource)
            {
                SearchSourceComboBox.SelectedItem = cbi;
                break;
            }
        }

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

    private async void SpotifySecretInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        // TODO: encryption?
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientSecret, SpotifySecretInput.Password.Trim());
        await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));
    }

    private async void DeezerARLInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerARL, DeezerARLInput.Password.Trim());
        await DeezerApi.InitDeezerClient(DeezerARLInput.Password.Trim());
    }

    private async void QobuzIDInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzId, QobuzIDInput.Text.Trim());
        QobuzApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken));
    }

    private async void QobuzTokenInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzToken, QobuzTokenInput.Password.Trim());
        var id = QobuzIDInput.Text.Trim();
        var token = QobuzTokenInput.Password.Trim();
        Thread t = new Thread(() =>
        {
            QobuzApi.Initialize(id, token);
        });
        t.Start();
    }

    private async void SearchSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchSourceComboBox.SelectedItem == null)
        {
            return;
        }

        await localSettings.SaveSettingAsync(SettingsViewModel.SearchSource, (SearchSourceComboBox.SelectedItem as ComboBoxItem).Content.ToString());
    }

    private async void SelectFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        FolderPicker openPicker = new Windows.Storage.Pickers.FolderPicker();

        // Retrieve the window handle of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the folder picker with the window handle.
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a folder
        StorageFolder folder = await openPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder); // Save the folder for future access

            // Set the folder path in the text box
            FolderTextBox.Text = folder.Path;
        }
    }

    private async void FolderTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var path = (sender as TextBox).Text;
        if (Directory.Exists(path) && Path.IsPathRooted(path))
        {
            // Save to settings
            await localSettings.SaveSettingAsync(SettingsViewModel.DownloadDirectory, path);
            Debug.WriteLine("Saved download directory: " + path);
        }
    }

    private async void AskToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AskBeforeDownload, isToggled);
    }
}