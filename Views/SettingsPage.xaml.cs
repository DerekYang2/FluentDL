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
using Microsoft.UI.Xaml.Controls.Primitives;
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
        // Set sliders
        DownloadThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.DownloadThreads) ?? 1;
        CommandThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.CommandThreads) ?? 1;

        // Set quality combo boxes
        DeezerQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.DeezerQuality) ?? 0;
        QobuzQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.QobuzQuality) ?? 0;
        SpotifyQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.SpotifyQuality) ?? 0;
        YoutubeQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.YoutubeQuality) ?? 0;

        // Set download directory
        LocationCard.Description = await localSettings.ReadSettingAsync<string>(SettingsViewModel.DownloadDirectory) ?? "No folder selected";
        if (string.IsNullOrWhiteSpace(LocationCard.Description.ToString())) LocationCard.Description = "No folder selected";

        // Set ToggleSwitches
        AskToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AskBeforeDownload);
        OverwriteToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Overwrite);

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

        // Set search checkboxes
        SearchAddCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchAddChecked);
        SearchShareCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchShareChecked);
        SearchOpenCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchOpenChecked);
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

            if (Directory.Exists(folder.Path) && Path.IsPathRooted(folder.Path))
            {
                // Set the folder path in the text box
                LocationCard.Description = folder.Path;
                // Save to settings
                await localSettings.SaveSettingAsync(SettingsViewModel.DownloadDirectory, folder.Path);
                Debug.WriteLine("Saved download directory: " + folder.Path);
            }
            else
            {
                LocationCard.Description = "Invalid folder path";
                Debug.WriteLine("Invalid folder path: " + folder.Path); // TODO: replace with infobar or dialog
            }
        }
    }

    private async void AskToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AskBeforeDownload, isToggled);
    }

    private void OverwriteToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        localSettings.SaveSettingAsync(SettingsViewModel.Overwrite, isToggled);
    }

    private async void DeezerQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 -> MP3 128kbps
        // 1 -> MP3 320kbps
        // 2 -> FLAC 1411kbps

        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerQuality, DeezerQualityComboBox.SelectedIndex);
    }

    private async void QobuzQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /*
           "5" -> MP3 320kbps CBR
           "6" -> FLAC 16bit/44.1kHz
           "7" -> FLAC 24bit/96kHz
           "27" -> FLAC 24bit/192kHz
         */

        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzQuality, QobuzQualityComboBox.SelectedIndex);
    }

    private async void YoutubeQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 - opus
        // 1 - flac (opus)
        // 2 - m4a (aac)

        await localSettings.SaveSettingAsync(SettingsViewModel.YoutubeQuality, YoutubeQualityComboBox.SelectedIndex);
    }

    private async void SpotifyQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 - spotify mp3
        // 1 - spotify flac
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyQuality, SpotifyQualityComboBox.SelectedIndex);
    }

    private void ComboBox_OnDropDownOpened(object? sender, object e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox == null || comboBox.SelectedItem == null)
        {
            return;
        }

        if (comboBox.SelectedItem is ComboBoxItem item) // Set placeholder to string to prevent collapse
        {
            comboBox.PlaceholderText = item.Content.ToString();
        }
    }

    // Save the value when the slider loses focus
    private async void CommandThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.CommandThreads, value);
    }

    private async void DownloadThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.DownloadThreads, value);
    }

    /*
     *                                <CheckBox x:Name="SearchAddCheckbox" Content="Add to queue" Checked="Search_OnChecked" Unchecked="Search_OnUnchecked" Tag="0"/>
       <CheckBox x:Name="SearchShareCheckbox" Content="Share link" Checked="Search_OnChecked" Unchecked="Search_OnUnchecked" Tag="1"/>
       <CheckBox x:Name="SearchOpenCheckbox" Content="Open" Checked="Search_OnChecked" Unchecked="Search_OnUnchecked" Tag="2"/>
     */
    private async void Search_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchAddChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchShareChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchOpenChecked, true);
                break;
        }
    }

    private void Search_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                localSettings.SaveSettingAsync(SettingsViewModel.SearchAddChecked, false);
                break;
            case "1":
                localSettings.SaveSettingAsync(SettingsViewModel.SearchShareChecked, false);
                break;
            case "2":
                localSettings.SaveSettingAsync(SettingsViewModel.SearchOpenChecked, false);
                break;
        }
    }
}