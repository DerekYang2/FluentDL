using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using CommunityToolkit.WinUI;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using FluentDL.Models;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentDL.Views;

public delegate void AuthenticationCallback(bool success); // Callback for conversion updates

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;
    private ILocalSettingsService localSettings;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        localSettings = App.GetService<ILocalSettingsService>();

        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        this.Loaded += SettingsPage_Loaded;

        dispatcher = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Set sliders
        ConversionThreadsSlider.ValueChanged += (s, e) =>
        {
            ConversionThreadsCard.Description = $"{(int)Math.Round(ConversionThreadsSlider.Value)} threads";
        };
        CommandThreadsSlider.ValueChanged += (s, e) =>
        {
            CommandThreadsCard.Description = $"{(int)Math.Round(CommandThreadsSlider.Value)} threads";
        };
        AudioConversionThreadsSlider.ValueChanged += (s, e) =>
        {
            AudioConversionThreadsCard.Description = $"{(int)Math.Round(AudioConversionThreadsSlider.Value)} threads";
        };

        CommandThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.CommandThreads) ?? 1;
        ConversionThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.ConversionThreads) ?? 3;
        AudioConversionThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.AudioConversionThreads) ?? 6; 

        // Set quality combo boxes (default flac)
        DeezerQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.DeezerQuality) ?? 2;
        QobuzQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.QobuzQuality) ?? 3;
        SpotifyQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.SpotifyQuality) ?? 1;
        YoutubeQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.YoutubeQuality) ?? 1;

        // Set download directory
        LocationCard.Description = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.DownloadDirectory) ?? "No folder selected";
        if (string.IsNullOrWhiteSpace(LocationCard.Description.ToString())) LocationCard.Description = "No folder selected";

        // Set FFmpeg path
        FFmpegPathCard.Description = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.FFmpegPath) ?? "No folder selected";
        if (string.IsNullOrWhiteSpace(FFmpegPathCard.Description.ToString())) FFmpegPathCard.Description = "No folder selected";

        // Set ToggleSwitches
        AskToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AskBeforeDownload);
        OverwriteToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Overwrite);
        NotificationsToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Notifications);
        AutoPlayToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AutoPlay);

        // Set Ids/Secrets
        ClientIdInput.Text = (await localSettings.ReadSettingAsync<string?>(SettingsViewModel.SpotifyClientId)) ?? "";
        SpotifySecretInput.Password = (await localSettings.ReadSettingAsync<string?>(SettingsViewModel.SpotifyClientSecret)) ?? "";
        DeezerARLInput.Password = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.DeezerARL) ?? "";
        QobuzIDInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzId) ?? "";
        QobuzTokenInput.Password = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzToken) ?? "";
        QobuzEmailInput.Text = AesHelper.Decrypt(await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzEmail) ?? "");
        QobuzPasswordInput.Password = AesHelper.Decrypt(await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzPassword) ?? "");

        // Set search checkboxes
        SearchAddCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchAddChecked);
        SearchShareCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchShareChecked);
        SearchOpenCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchOpenChecked);

        // Set local explorer checkboxes
        LocalExplorerAddCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerAddChecked);
        LocalExplorerEditCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerEditChecked);
        LocalExplorerOpenCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerOpenChecked);

        // Set queue checkboxes
        QueueShareCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueShareChecked);
        QueueDownloadCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueDownloadChecked);
        QueueDownloadCoverCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueDownloadCoverChecked);
        QueueRemoveCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueRemoveChecked);
    }

    private async void SpotifyUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientId, ClientIdInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientSecret, SpotifySecretInput.Password.Trim());
        await SpotifyApi.Initialize(await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId), await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret));

        if (SpotifyApi.IsInitialized)
        {
            ShowInfoBar(InfoBarSeverity.Success, "Authentication successful", 3, "Spotify");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Error, "Authentication failed", 3, "Spotify");
        }
    }

    private async void DeezerUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerARL, DeezerARLInput.Password.Trim());
        await DeezerApi.InitDeezerClient(DeezerARLInput.Password.Trim());

        if (DeezerApi.IsInitialized)
        {
            ShowInfoBar(InfoBarSeverity.Success, "Authentication successful", 3, "Deezer");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Error, "Authentication failed", 3, "Deezer");
        }
    }

    private async void QobuzUpdateButton_Click(object sender, RoutedEventArgs e) {
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzEmail, AesHelper.Encrypt(QobuzEmailInput.Text.Trim()));
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzPassword, AesHelper.Encrypt(QobuzPasswordInput.Password.Trim()));
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzId, QobuzIDInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzToken, QobuzTokenInput.Password.Trim());

        AuthenticationCallback authCallback = (bool success) =>
        {
            dispatcher.TryEnqueue(() =>
            {
                if (success)
                {
                    ShowInfoBar(InfoBarSeverity.Success, "Authentication successful", 3, "Qobuz");
                }
                else
                {
                    ShowInfoBar(InfoBarSeverity.Error, "Authentication failed", 3, "Qobuz");
                }
            });
        };

        Thread thread = new Thread(() =>
        {
            var qobuzEmail = AesHelper.Decrypt(localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzEmail).GetAwaiter().GetResult() ?? "");
            var qobuzPassword = AesHelper.Decrypt(localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzPassword).GetAwaiter().GetResult() ?? "");
            var qobuzId = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId).GetAwaiter().GetResult();
            var qobuzToken = localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken).GetAwaiter().GetResult();
            QobuzApi.Initialize(qobuzEmail, qobuzPassword, qobuzId, qobuzToken, authCallback);
        });
        thread.Priority = ThreadPriority.Highest;
        thread.Start();
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
                await localSettings.SaveSettingAsync<string?>(SettingsViewModel.DownloadDirectory, folder.Path);
                ShowInfoBar(InfoBarSeverity.Success, $"Set download directory to <a href='{folder.Path}'>{folder.Path}</a>");
            }
            else
            {
                ShowInfoBar(InfoBarSeverity.Error, "Invalid folder path");
            }
        }
    }

    private async void SelectFFmpegButton_OnClick(object sender, RoutedEventArgs e)
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
                FFmpegPathCard.Description = folder.Path;
                await localSettings.SaveSettingAsync(SettingsViewModel.FFmpegPath, folder.Path);
                // Update ffmpeg runner
                await FFmpegRunner.Initialize();

                ShowInfoBar(InfoBarSeverity.Success, $"Set FFmpeg path to <a href='{folder.Path}'>{folder.Path}</a>");
            }
            else
            {
                FFmpegPathCard.Description = "Invalid folder path";

                ShowInfoBar(InfoBarSeverity.Error, "Invalid folder path");
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

    private async void ConversionThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.ConversionThreads, value);
    }

    private void AudioConversionThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        localSettings.SaveSettingAsync(SettingsViewModel.AudioConversionThreads, value);
    }

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

    private async void Search_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchAddChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchShareChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchOpenChecked, false);
                break;
        }
    }

    private async void LocalExplorer_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerAddChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerEditChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerOpenChecked, true);
                break;
        }
    }

    private async void LocalExplorer_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerAddChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerEditChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerOpenChecked, false);
                break;
        }
    }

    private async void Queue_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkbox = sender as CheckBox;
        var index = checkbox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueShareChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadCoverChecked, true);
                break;
            case "3":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueRemoveChecked, true);
                break;
        }
    }

    private async void Queue_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkbox = sender as CheckBox;
        var index = checkbox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueShareChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadCoverChecked, false);
                break;
            case "3":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueRemoveChecked, false);
                break;
        }
    }

    private async void NotificationsToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.Notifications, isToggled);
    }
    private async void AutoPlayToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AutoPlay, isToggled);
    }

    private async void ResetFFmpegButton_OnClick(object sender, RoutedEventArgs e)
    {
        FFmpegPathCard.Description = "No folder selected";
        await localSettings.SaveSettingAsync(SettingsViewModel.FFmpegPath, "");
        // Update ffmpeg runner
        await FFmpegRunner.Initialize();

        ShowInfoBar(InfoBarSeverity.Informational, "Reset to using built-in Ffmpeg");
    }

    // Infobar helper methods ---------------------------------------------------
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object? sender, object e)
    {
        if (sender == null) return;
        PageInfoBar.Opacity = 0;
        (sender as DispatcherTimer).Stop();
        // Set IsOpen to false after 0.25 seconds
        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            dispatcher.TryEnqueue(() =>
            {
                PageInfoBar.IsOpen = false;
            });
        });
    }

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2, string title = "")
    {
        title = title.Trim();
        message = message.Trim();
        dispatcher.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            //PageInfoBar.Title = title;

            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>   {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
        });
        dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
        dispatcherTimer.Start();
    }
}