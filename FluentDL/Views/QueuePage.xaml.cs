﻿using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.AppService;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using ABI.Windows.Storage.Pickers;
using ABI.Windows.UI.ApplicationSettings;
using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkit.WinUI.UI.Controls.TextToolbarSymbols;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Color = Windows.UI.Color;
using FileSavePicker = Windows.Storage.Pickers.FileSavePicker;
using Symbol = Microsoft.UI.Xaml.Controls.Symbol;
using System.Diagnostics.Metrics;
using ABI.Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using FolderPicker = ABI.Windows.Storage.Pickers.FolderPicker;
using FontWeights = Microsoft.UI.Text.FontWeights;
using Newtonsoft.Json;

namespace FluentDL.Views;

// Converter converts integer (queue list count) to a message
public class QueueMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if ((int)value == 0)
        {
            return "No tracks in queue";
        }

        return value + " tracks in queue";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class PathToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public delegate void ConversionUpdateCallback(InfoBarSeverity severity, SongSearchObject song, string? location = null); // Callback for conversion updates

public sealed partial class QueuePage : Page
{
    public delegate void QueueRunCallback(InfoBarSeverity severity, string message);

    // Create callback
    private QueueRunCallback queueRunCallback;

    // Create dispatcher queue
    private DispatcherQueue dispatcherQueue;
    private DispatcherTimer dispatcherTimer;
    private CancellationTokenSource cancellationTokenSource;

    // Conversion variables
    private HashSet<string> selectedSources;
    private ConcurrentDictionary<SongSearchObject, int> successSource, warningSource, errorSource;

    private static readonly object _lock = new object();
    private static bool _isConverting = false;

    public static bool IsConverting
    {
        get
        {
            lock (_lock)
            {
                return _isConverting;
            }
        }
        set
        {
            lock (_lock)
            {
                _isConverting = value;
            }
        }
    }

    public QueueViewModel ViewModel
    {
        get;
    }

    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;

        CustomListView.ItemsSource = QueueViewModel.Source;
        cancellationTokenSource = new CancellationTokenSource();
        InitPreviewPanelButtons();
        InitializeAnimations();
        StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button initially
        OutputComboBox.ItemsSource = new List<string>
        {
            "Deezer",
            "Qobuz",
            "Spotify",
            "YouTube",
            "Local"
        }; // Default list

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            OnQueueSourceChange();
        };

        NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = QueueViewModel.Source.Count > 0;

        // Set conversion variables
        successSource = new ConcurrentDictionary<SongSearchObject, int>();
        warningSource = new ConcurrentDictionary<SongSearchObject, int>();
        errorSource = new ConcurrentDictionary<SongSearchObject, int>();

        selectedSources = new HashSet<string>();

        // Create callback
        queueRunCallback = (severity, message) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                ShowInfoBar(severity, message);
                SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications).ContinueWith((task) =>
                {
                    var showNotif = task.Result ?? false;

                    if (showNotif)
                    {
                        App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
                    }
                });
            });
        };
    }

    private void InitializeAnimations()
    {
        AnimationHelper.AttachSpringDownAnimation(DownloadAllButton, DownloadAllButtonIcon);
        AnimationHelper.AttachSpringRightAnimation(ConvertDialogOpenButton, ConvertDialogOpenIcon);
        AnimationHelper.AttachScaleAnimation(CommandButton, CommandIcon);
        AnimationHelper.AttachScaleAnimation(ClearButton, ClearIcon);
        AnimationHelper.AttachScaleAnimation(StartStopButton, StartStopIcon);
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong != null)
        {
            PreviewPanel.Show();
            if (selectedSong.Source == "local")
            {
                await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
            }
            else
            {
                await PreviewPanel.Update(selectedSong);
            }
        }
        
        await QueueViewModel.UpdateShortcutVisibility(); // Initialize the settings for shortcut buttons
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Clear preview 
        PreviewPanel.Clear();
    }

    private async void OutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        if (button != null)
        {
            // Get the data context of the button (the item in the ListView)

            if (button.DataContext is QueueObject queueObject)
            {
                OutputDialog.XamlRoot = this.XamlRoot;
                OutputMessage.Text = $"Terminal output for track \"{queueObject.Title}\":";
                OutputTextBox.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, queueObject.ResultString);
                await OutputDialog.ShowAsync();
            }
        }
    }

    private void OnQueueSourceChange()
    {
        if (IsConverting) // Ignore the below
        {
            return;
        }

        dispatcherQueue.TryEnqueue(() =>
        {
            // Check if pause was called
            if (cancellationTokenSource.Token.IsCancellationRequested)
            {
                SetContinueUI(); // If paused, UI should show continue
                ShowInfoBar(InfoBarSeverity.Informational, "Queue paused");
            }

            if (QueueViewModel.Source.Count == 0)
            {
                CountText.Text = "No tracks in queue";
                QueueProgress.Value = 0;
                StartStopButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                var completedCount = QueueViewModel.GetCompletedCount();
                QueueProgress.Value = 100.0 * completedCount / QueueViewModel.Source.Count;
                ProgressText.Text = $"Completed {QueueViewModel.GetCompletedCount()} of {QueueViewModel.Source.Count}";

                if (completedCount == QueueViewModel.Source.Count) // If all tracks are completed
                {
                    // Reset UI to normal
                    ProgressTextButton.Visibility = StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button
                    CountTextButton.Visibility = Visibility.Visible;
                    EnableButtons();
                    QueueProgress.Value = 0;
                }
            }

            NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearButton.IsEnabled = QueueViewModel.Source.Count > 0 && !QueueViewModel.IsRunning;
        });
    }

    private void InitPreviewPanelButtons()
    {
        var shareLinkButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Link), Label = "Link" };
        shareLinkButton.Click += (sender, e) => CopySongLink(PreviewPanel.GetSong());

        var downloadCoverButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uEE71" }, Label = "Save Cover" };
        downloadCoverButton.Click += async (sender, e) => await DownloadSongCover(PreviewPanel.GetSong());

        var removeButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uECC9" }, Label = "Remove" };
        removeButton.Click += async (sender, e) => await RemoveSongFromQueue(PreviewPanel.GetSong());

        AnimationHelper.AttachScaleAnimation(shareLinkButton);
        AnimationHelper.AttachSpringUpAnimation(downloadCoverButton);
        AnimationHelper.AttachScaleAnimation(removeButton);

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { downloadCoverButton, shareLinkButton, removeButton });
    }

    private void ShareLinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;

        CopySongLink(song);
    }

    private async void DownloadCoverButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;

        await DownloadSongCover(song);
    }

    private async void RemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;

        await RemoveSongFromQueue(song);
    }

    private void OpenLocalButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;

        if (song != null)
        {
            var argument = $"/select, \"{song.Id}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
    }

    private void OpenSpekButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;
        if (song == null || song.Source != "local" || !File.Exists(song.Id))  // SongSearchObject.id for local tracks is the file path
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Track does not exist locally", 3);
            return;
        }

        SpekRunner.RunSpek(song.Id);
    }

    private void CopySongLink(SongSearchObject? song)
    {
        if (song == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to copy source to clipboard");
            return;
        }

        var uri = ApiHelper.GetUrl(song);

        Clipboard.CopyToClipboard(uri);
        ShowInfoBar(InfoBarSeverity.Success, "Copied link to clipboard");
    }

    private async Task RemoveSongFromQueue(SongSearchObject? selectedSong)
    {
        if (selectedSong == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Error removing track from queue", 3);
            return;
        }

        if (IsConverting || QueueViewModel.IsRunning)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Cannot remove track while queue is running", 3);
            return;
        }

        QueueViewModel.Remove(selectedSong);
        PreviewPanel.Clear();
        await QueueViewModel.SaveQueue(); // Save the queue to file
    }

    private async Task DownloadSongCover(SongSearchObject? songObj)
    {
        // Check if the image is null
        if (songObj == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to download cover");
            return;
        }

        // Create file name
        var firstArtist = songObj.Artists.Split(",")[0].Trim();
        var isrcStr = !string.IsNullOrWhiteSpace(songObj.Isrc) ? $" [{songObj.Isrc}] " : "";
        var safeFileName = ApiHelper.GetSafeFilename($"{songObj.TrackPosition}. {firstArtist} - {songObj.Title}{isrcStr}Cover Art.jpg");

        // Create a file save picker
        FileSavePicker savePicker = new() { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = safeFileName, };
        savePicker.FileTypeChoices.Add("Image", new List<string>() { ".jpg", ".png" });

        // Retrieve the window handle (HWND) of the current WinUI 3 window
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the file picker with the window handle (HWND)
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

        var file = await savePicker.PickSaveFileAsync();

        // Save bitmap image as jpg/png
        if (file != null)
        {
            byte[]? coverBytes;
            if (songObj.Source == "local")
            {
                coverBytes = await Task.Run(() => LocalExplorerViewModel.GetAlbumArtBytes(songObj.Id));
            }
            else
            {
                try
                {
                    var bitmapImg = PreviewPanel.GetImage();
                    coverBytes = await new HttpClient().GetByteArrayAsync(bitmapImg?.UriSource);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    return;
                }
            }

            if (coverBytes == null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Failed to save cover", 5);
                return;
            }

            await File.WriteAllBytesAsync(file.Path, coverBytes);
            ShowInfoBar(InfoBarSeverity.Success, $"Cover saved to <a href='{file.Path}'>{file.Name}</a>", 5);
        }

        //await DeezerApi.DownloadTrack(await DeezerApi.GetTrack(PreviewPanel.GetSong().Id), "E:\\Other Downloads\\test");
    }

    private async void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            PreviewPanel.Clear();
            return;
        }

        PreviewPanel.Show();
        if (selectedSong.Source == "local")
        {
            await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
        }
        else
        {
            await PreviewPanel.Update(selectedSong);
        }
    }

    private async void CommandButton_OnClick(object sender, RoutedEventArgs e)
    {
        CustomCommandDialog.XamlRoot = this.XamlRoot;

        // Set latest command used if null
        if (CommandInput.Text == null || CommandInput.Text.Trim().Length == 0)
        {
            CommandInput.Text = await LocalCommands.GetLatestCommand();
        }

        // Set latest path used if null
        if (DirectoryInput.Text == null || DirectoryInput.Text.Trim().Length == 0)
        {
            DirectoryInput.Text = await LocalCommands.GetLatestPath();
        }

        await CustomCommandDialog.ShowAsync();
    }

    private async void CustomCommandDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var commandInputText = CommandInput.Text.Trim();
        var directoryInputText = DirectoryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(commandInputText) || QueueViewModel.IsRunning || QueueViewModel.Source.Count == 0) // If the command is empty or the queue is currently running, return
        {
            return;
        }
        StartStopButton.Visibility = Visibility.Visible; // Display start stop
        QueueViewModel.SetCommand(commandInputText);
        QueueViewModel.Reset(); // Reset the queue object result strings and index

        cancellationTokenSource = new CancellationTokenSource();
        await QueueViewModel.RunCommand(directoryInputText, cancellationTokenSource.Token, queueRunCallback);
        SetPauseUI();

        LocalCommands.AddCommand(commandInputText); // Add the command to the previous command list
        LocalCommands.AddPath(directoryInputText); // Add the path to the previous path list

        await LocalCommands.SaveLatestCommand(commandInputText);
        await LocalCommands.SaveLatestPath(directoryInputText);
        await LocalCommands.SaveCommands();
        await LocalCommands.SavePaths();
    }

    private async void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        QueueViewModel.Clear();
        StartStopButton.Visibility = Visibility.Collapsed;
        ShowInfoBar(InfoBarSeverity.Informational, "Queue cleared");
        await QueueViewModel.SaveQueue();
    }

    private async void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        const string pauseText = "Pausing ...";
        if (StartStopText.Text.Equals(pauseText)) // If already attempted a pause
        {
            return;
        }

        if (QueueViewModel.IsRunning) // If the queue is running, cancel 
        {
            cancellationTokenSource.Cancel();
            StartStopText.Text = pauseText;
        }
        else // If the queue is not running, start
        {
            cancellationTokenSource = new CancellationTokenSource();
            SetPauseUI();
            await QueueViewModel.RunCommand(DirectoryInput.Text, cancellationTokenSource.Token, queueRunCallback);
        }
    }

    private void SetContinueUI()
    {
        // UI states when queue is paused
        StartStopIcon.Glyph = "\uE768"; // Change the icon to a start icon
        StartStopText.Text = "Continue";
        EnableButtons();
        ProgressTextButton.Visibility = Visibility.Collapsed;
        CountTextButton.Visibility = Visibility.Visible;
    }

    private void SetPauseUI()
    {
        // UI states when the queue is running
        StartStopIcon.Glyph = "\uE769"; // Change the icon to a pause icon
        StartStopText.Text = "Pause";
        DisableButtons();
        ProgressTextButton.Visibility = Visibility.Visible;
        CountTextButton.Visibility = Visibility.Collapsed;
    }

    private void EnableButtons()
    {
        DownloadAllButton.IsEnabled = true;
        CommandButton.IsEnabled = true;
        ConvertDialogOpenButton.IsEnabled = true;
        ClearButton.IsEnabled = true;
    }

    private void DisableButtons()
    {
        DownloadAllButton.IsEnabled = false;
        CommandButton.IsEnabled = false;
        ConvertDialogOpenButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
    }

    private void CommandInput_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var suitableItems = new List<string>();
            var inputLower = sender.Text.ToLower().Trim();

            foreach (var command in LocalCommands.GetCommandList())
            {
                if (suitableItems.Count >= 10) // Limit the number of suggestions to 10
                {
                    break;
                }

                if (command.ToLower().Trim().StartsWith(inputLower))
                {
                    suitableItems.Add(command);
                }
            }

            if (suitableItems.Count == 0)
            {
                suitableItems.Add("No command found");
            }

            sender.ItemsSource = suitableItems;
        }
    }

    private void CommandInput_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = args.SelectedItem.ToString(); // Set the text to the chosen suggestion
    }

    private void DirectoryInput_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var suitableItems = new List<string>();
            var inputLower = sender.Text.ToLower();
            // Remove everything except letters and numbers
            inputLower = Regex.Replace(inputLower, "[^a-zA-Z0-9]", "");

            foreach (var command in LocalCommands.GetPathList())
            {
                if (suitableItems.Count >= 10) // Limit the number of suggestions to 10
                {
                    break;
                }

                if (Regex.Replace(command.ToLower(), "[^a-zA-Z0-9]", "").Contains(inputLower)) // Compare only alphanumeric characters
                {
                    suitableItems.Add(command);
                }
            }

            if (suitableItems.Count == 0)
            {
                suitableItems.Add("No directory found");
            }

            sender.ItemsSource = suitableItems;
        }
    }

    private void DirectoryInput_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = args.SelectedItem.ToString(); // Set the text to the chosen suggestion
    }

    // Required for animation to work
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2, string title = "")
    {
        title = title.Trim();
        message = message.Trim();
        dispatcherQueue.TryEnqueue(() =>
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

    private void ShowInfoBarPermanent(InfoBarSeverity severity, string message, string title = "")
    {
        title = title.Trim();
        message = message.Trim();
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            //PageInfoBar.Title = title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>    {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
        });
    }

    private void ForceHideInfoBar()
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.Opacity = 0;
            if (dispatcherTimer.IsEnabled)
            {
                dispatcherTimer.Stop();
            }

            // Set IsOpen to false after 0.25 seconds
            Task.Factory.StartNew(() =>
            {
                System.Threading.Thread.Sleep(250);
                try
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        PageInfoBar.IsOpen = false;
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            });
        });
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object sender, object e)
    {
        var timer = sender as DispatcherTimer;
        if (timer == null || !timer.IsEnabled) return; // Already closed

        PageInfoBar.Opacity = 0;
        timer.Stop();

        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            try // Can crash if app is closed
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    PageInfoBar.IsOpen = false;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        });
    }

    private void OutputDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string text;
        OutputTextBox.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out text); // Get the text from the RichEditBox
        Clipboard.CopyToClipboard(text);
        ShowInfoBar(InfoBarSeverity.Success, "Copied to clipboard");
    }

    private void CheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var checkBoxContent = checkBox.Content.ToString();
        selectedSources.Add(checkBoxContent.ToLower());
    }

    private void CheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var checkBoxContent = checkBox.Content.ToString();
        selectedSources.Remove(checkBoxContent.ToLower());
    }

    private string? GetOutputSource()
    {
        return OutputComboBox.SelectedItem as string;
    }

    private async void ConvertDialogOpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Pre-select existing sources
        var sources = new HashSet<string>();
        foreach (var song in (ObservableCollection<QueueObject>)CustomListView.ItemsSource)
        {
            sources.Add(song.Source);
        }

        DeezerCheckBox.IsChecked = sources.Contains("deezer");
        QobuzCheckBox.IsChecked = sources.Contains("qobuz");
        SpotifyCheckBox.IsChecked = sources.Contains("spotify");
        YouTubeCheckBox.IsChecked = sources.Contains("youtube");
        LocalCheckBox.IsChecked = sources.Contains("local");

        // If unchecked, disable the output combobox
        DeezerCheckBox.IsEnabled = DeezerCheckBox.IsChecked.Value;
        QobuzCheckBox.IsEnabled = QobuzCheckBox.IsChecked.Value;
        SpotifyCheckBox.IsEnabled = SpotifyCheckBox.IsChecked.Value;
        YouTubeCheckBox.IsEnabled = YouTubeCheckBox.IsChecked.Value;
        LocalCheckBox.IsEnabled = LocalCheckBox.IsChecked.Value;

        // Show conversion dialog
        ConversionDialog.XamlRoot = this.XamlRoot;
        await ConversionDialog.ShowAsync();
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Create a queue with the indexes of sources to process
        ConcurrentQueue<int> indexQueue = new ConcurrentQueue<int>();

        // Loop through and find the total number of queries to process
        for (int i = 0; i < QueueViewModel.Source.Count; i++)
        {
            var song = QueueViewModel.Source[i];
            if (song.Source != "local") // If source is not already local
            {
                indexQueue.Enqueue(i);
            }
        }
        int totalCount = indexQueue.Count;

        if (totalCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to download", 3);
            return;
        }
        // Set download location if output is local and ask before download is enabled or download directory is not set
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);

        if (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory))
        {
            directory = await StoragePickerHelper.GetDirectory();
            if (directory == null)
            {
                ShowInfoBar(InfoBarSeverity.Warning, "No download directory selected", 3);
                return;
            }
        }

        // Clear preview pane when source is being edited
        PreviewPanel.Clear();

        // Clear queue command running progress
        QueueViewModel.Reset(); // Reset the queue object result strings and index
        cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source

        // Prepare UI
        DownloadAllButton.Visibility = CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Collapsed; // Hide buttons
        ConvertStopButton.Visibility = Visibility.Visible; // Show stop button
        // Clear the conversion results
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        QueueViewModel.ResetConversionResults(); // Clear all badges and results on queue objects
        ViewModel.SuccessCount = ViewModel.WarningCount = ViewModel.ErrorCount = 0; // Reset the counts

        ConversionUpdateCallback conversionUpdateCallback = (severity, song, location) =>
        {
            switch (severity)
            {
                case InfoBarSeverity.Success:
                    successSource.TryAdd(song, 0);
                    break;
                case InfoBarSeverity.Warning:
                    warningSource.TryAdd(song, 0);
                    break;
                case InfoBarSeverity.Error:
                    errorSource.TryAdd(song, 0);
                    break;
                default:
                    throw new Exception("Unspecified severity in callback");
            }
        };

        // Infobar notification
        ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Saving {totalCount} tracks to <a href='{directory}'>{directory}</a>", title: "Download in Progress");

        var ogVal = QueueProgress.Value; // Save the original value of the progress bar
        QueueProgress.Value = 0; // Set to 0

        int processedCount = 0;
        var token = cancellationTokenSource.Token;
        IsConverting = true;

        var EndConversionLambda = async () =>
        {
            DownloadAllButton.Visibility = CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Visible; // Show buttons
            ConvertStopText.Text = "Stop"; // Reset stop button text
            ConvertStopButton.Visibility = Visibility.Collapsed; // Hide stop button
            QueueProgress.Value = ogVal; // Reset the progress bar
            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications) ?? false)
            {
                App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
            }

            ForceHideInfoBar();

            await QueueViewModel.SaveQueue();
        };

        var downloadThreads = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.ConversionThreads) ?? 1;

        for (int threadNum = 0; threadNum < downloadThreads; threadNum++)
        {
            Thread t = new Thread(async () =>
            {
                while (IsConverting)
                {
                    if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                    {
                        IsConverting = false;
                        dispatcherQueue.TryEnqueue(async () => await EndConversionLambda());
                        return;
                    }

                    if (indexQueue.IsEmpty) // If the queue is already empty, end this thread
                    {
                        return;
                    }

                    int i = indexQueue.TryDequeue(out var iVal) ? iVal : -1; // Get the index from the queue

                    if (i == -1) // If the index is invalid, skip
                    {
                        continue;
                    }

                    var song = QueueViewModel.Source[i];

                    // Set song to running (progress ring will appear)
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        song.IsRunning = true;
                        QueueViewModel.Source[i] = song;
                    });

                    SongSearchObject? newSongObj = null;
                    string fileLocation = await ApiHelper.DownloadObject(song, directory, conversionUpdateCallback);
                    newSongObj = song;

                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        if (newSongObj != null)
                        {
                            QueueObject? queueObj;

                            var localSong = LocalExplorerViewModel.ParseFile(fileLocation);

                            if (localSong != null)
                            {
                                localSong.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(fileLocation);
                                queueObj = await QueueViewModel.CreateQueueObject(localSong);
                            }
                            else // Can happen when trying to parse opus files
                            {
                                queueObj = QueueViewModel.Source[i]; // Set to the initial object so badge is shown below
                            }


                            if (queueObj != null)
                            {
                                if (successSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                                else if (warningSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 225, 0));
                                else // Assume failure
                                {
                                    queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                }

                                queueObj.IsRunning = false; // Set song to not running
                                QueueViewModel.Replace(i, queueObj);
                            }

                            // Save state of the queue in case of crashes?
                            await QueueViewModel.SaveQueue();
                        }
                        else // Failed conversion, set current object badge to error
                        {
                            song.IsRunning = false; // Set song to not running

                            if (!token.IsCancellationRequested) // If token is cancelled, don't show error badge
                            {
                                song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                            }

                            QueueViewModel.Replace(i, song);
                        }
                    });

                    var processCaptured = Interlocked.Increment(ref processedCount); // Increment the processed count

                    dispatcherQueue.TryEnqueue(() =>
                    {
                        QueueProgress.Value = 100.0 * processCaptured / totalCount; // Update the progress bar
                    });

                    if (processCaptured == totalCount) // If all tracks are processed
                    {
                        IsConverting = false;
                        dispatcherQueue.TryEnqueue(async () => await EndConversionLambda());
                        return;
                    }
                }
            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }
    }

    private async void ConversionDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Check for valid input
        if (GetOutputSource() == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Please select an output source.", 3);
            return;
        }

        if (DeezerCheckBox.IsChecked == false && QobuzCheckBox.IsChecked == false && SpotifyCheckBox.IsChecked == false && YouTubeCheckBox.IsChecked == false && LocalCheckBox.IsChecked == false)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Please select at least one input source.", 3);
            return;
        }

        var outputSource = GetOutputSource().ToLower();

        // Create a queue with the indexes of sources to process
        ConcurrentQueue<int> indexQueue = new ConcurrentQueue<int>();

        // Loop through and find the total number of queries to process
        for (int i = 0; i < QueueViewModel.Source.Count; i++)
        {
            var song = QueueViewModel.Source[i];
            if (selectedSources.Contains(song.Source) && outputSource != song.Source) // If source is selected as input and isn't the same as output
            {
                indexQueue.Enqueue(i);
            }
        }
        int totalCount = indexQueue.Count;

        if (totalCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to convert", 3);
            return;
        }

        // Set download location if output is local and ask before download is enabled or download directory is not set
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);
        if (outputSource == "local" && (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory)))
        {
            directory = await StoragePickerHelper.GetDirectory();
            if (directory == null)
            {
                ShowInfoBar(InfoBarSeverity.Warning, "No download directory selected", 3);
                return;
            }
        }

        // Clear preview pane when source is being edited
        PreviewPanel.Clear();

        // Clear queue command running progress
        QueueViewModel.Reset(); // Reset the queue object result strings and index
        cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source

        // Prepare UI
        DownloadAllButton.Visibility = CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Collapsed; // Hide buttons
        ConvertStopButton.Visibility = Visibility.Visible; // Show stop button
        // Clear the conversion results
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        QueueViewModel.ResetConversionResults(); // Clear all badges and results on queue objects
        ViewModel.SuccessCount = ViewModel.WarningCount = ViewModel.ErrorCount = 0; // Reset the counts

        ConversionUpdateCallback conversionUpdateCallback = (severity, song, location) =>
        {
            switch (severity)
            {
                case InfoBarSeverity.Success:
                    successSource.TryAdd(song, 0);
                    break;
                case InfoBarSeverity.Warning:
                    warningSource.TryAdd(song, 0);
                    break;
                case InfoBarSeverity.Error:
                    errorSource.TryAdd(song, 0);
                    break;
                default:
                    throw new Exception("Unspecified severity in callback");
            }
        };

        // Infobar notification
        ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Converting selected input sources to {GetOutputSource()}", "Conversion in Progress");

        var ogVal = QueueProgress.Value; // Save the original value of the progress bar
        QueueProgress.Value = 0; // Set to 0

        int processedCount = 0;
        var token = cancellationTokenSource.Token;
        IsConverting = true;

        var EndConversionLambda = async () =>
        {
            DownloadAllButton.Visibility = CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Visible; // Show buttons
            ConvertStopText.Text = "Stop"; // Reset stop button text
            ConvertStopButton.Visibility = Visibility.Collapsed; // Hide stop button
            QueueProgress.Value = ogVal; // Reset the progress bar

            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications) ?? false)
            {
                App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
            }

            ForceHideInfoBar();
            await ShowConversionDialog();  // Show conversion results dialog
            await QueueViewModel.SaveQueue(); // Save the queue
        };

        var downloadThreads = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.ConversionThreads) ?? 1;

        for (int threadNum = 0; threadNum < downloadThreads; threadNum++)
        {
            Thread t = new Thread(async () =>
            {
                while (IsConverting)
                {
                    if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                    {
                        IsConverting = false;
                        dispatcherQueue.TryEnqueue(async () => await EndConversionLambda());
                        return;
                    }

                    if (indexQueue.IsEmpty) // If the queue is already empty, end this thread
                    {
                        return;
                    }

                    int i = indexQueue.TryDequeue(out var iVal) ? iVal : -1; // Get the index from the queue

                    if (i == -1) // If the index is invalid, skip
                    {
                        continue;
                    }

                    var song = QueueViewModel.Source[i];

                    // Set song to running (progress ring will appear)
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        song.IsRunning = true;
                        QueueViewModel.Source[i] = song;
                    });

                    SongSearchObject? newSongObj = null;
                    string? fileLocation = null;
                    if (outputSource == "local") // Conversion to local, download the track
                    {
                        fileLocation = await ApiHelper.DownloadObject(song, directory, conversionUpdateCallback);
                        newSongObj = song;
                    }
                    else
                    {
                        newSongObj = outputSource switch
                        {
                            "deezer" => await DeezerApi.GetDeezerTrack(song, token, conversionUpdateCallback),
                            "qobuz" => await QobuzApi.GetQobuzTrack(song, token, conversionUpdateCallback),
                            "spotify" => await SpotifyApi.GetSpotifyTrack(song, token, conversionUpdateCallback),
                            "youtube" => await YoutubeApi.GetYoutubeTrack(song, token, conversionUpdateCallback),
                            _ => throw new Exception("Unspecified output source") // Should never happen
                        };
                    }

                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        if (newSongObj != null)
                        {
                            QueueObject? queueObj;
                            if (outputSource == "local")
                            {
                                var localSong = LocalExplorerViewModel.ParseFile(fileLocation);

                                if (localSong != null)
                                {
                                    localSong.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(fileLocation);
                                    queueObj = await QueueViewModel.CreateQueueObject(localSong);
                                }
                                else // Can happen when trying to parse opus files
                                {
                                    queueObj = QueueViewModel.Source[i]; // Set to the initial object so badge is shown below
                                }
                            }
                            else
                            {
                                queueObj = await QueueViewModel.CreateQueueObject(newSongObj);
                            }

                            if (queueObj != null)
                            {
                                if (successSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                                else if (warningSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 225, 0));
                                else // Assume failure
                                {
                                    queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                }

                                queueObj.IsRunning = false; // Set song to not running
                                QueueViewModel.Replace(i, queueObj);
                            }
                        }
                        else // Failed conversion, set current object badge to error
                        {
                            song.IsRunning = false; // Set song to not running

                            if (!token.IsCancellationRequested) // If token is cancelled, don't show error badge
                            {
                                song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                            }

                            QueueViewModel.Replace(i, song);
                        }
                    });

                    var processCaptured = Interlocked.Increment(ref processedCount); // Increment the processed count

                    dispatcherQueue.TryEnqueue(() =>
                    {
                        QueueProgress.Value = 100.0 * processCaptured / totalCount; // Update the progress bar
                    });


                    if (processCaptured == totalCount) // If all tracks are processed
                    {
                        IsConverting = false;
                        dispatcherQueue.TryEnqueue(async () => await EndConversionLambda());
                        return;
                    }
                }
            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }
    }

    private void ConvertStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsConverting) // If currently converting
        {
            cancellationTokenSource.Cancel(); // Cancel the conversion
            ConvertStopText.Text = "Stopping ...";
        }
    }

    private void ConversionTabView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = ConversionTabView.SelectedItem as TabViewItem;
        // Set items source based on selected tab
        if (selectedItem == SuccessTab)
        {
            ConversionListView.ItemsSource = successSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Success;
            TabInfoBar.Content = "Exact conversions using ISRC or other ids";
            NoConversionText.Visibility = successSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == WarningTab)
        {
            ConversionListView.ItemsSource = warningSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Warning;
            TabInfoBar.Content = "Attempted conversions using metadata";
            NoConversionText.Visibility = warningSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == ErrorTab)
        {
            ConversionListView.ItemsSource = errorSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Error;
            TabInfoBar.Content = "Failed conversions";
            NoConversionText.Visibility = errorSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task ShowConversionDialog()
    {
        ConversionResultsDialog.XamlRoot = this.XamlRoot;

        // Set the counts
        ViewModel.SuccessCount = successSource.Count;
        ViewModel.WarningCount = warningSource.Count;
        ViewModel.ErrorCount = errorSource.Count;

        if (ViewModel.SuccessCount > 0)
        {
            ConversionTabView.SelectedItem = SuccessTab;
        }
        else if (ViewModel.WarningCount > 0)
        {
            ConversionTabView.SelectedItem = WarningTab;
        }
        else if (ViewModel.ErrorCount > 0)
        {
            ConversionTabView.SelectedItem = ErrorTab;
        }

        try
        {
            // Show the dialog
            await ConversionResultsDialog.ShowAsync();
        }
        catch (Exception e)
        {
            Debug.WriteLine("Queue conversion dialog fail: " + e.Message);
        }
    }
}