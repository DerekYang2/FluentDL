using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
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
using Microsoft.UI.Xaml.Navigation;
using FileSavePicker = Windows.Storage.Pickers.FileSavePicker;
using Symbol = Microsoft.UI.Xaml.Controls.Symbol;
namespace FluentDL.Views;

public partial class PathToVisibilityConverter : IValueConverter
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
        dispatcherTimer.Tick += DispatcherTimer_Tick;

        CustomListView.ItemsSource = QueueViewModel.Source;
        cancellationTokenSource = new CancellationTokenSource();
        InitPreviewPanelButtons();
        InitializeAnimations();
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
        AnimationHelper.AttachScaleAnimation(ClearButton, ClearIcon);
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e)
    {
        OnQueueSourceChange();

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
    
    private void SetCountText()
    {
        var count = QueueViewModel.Source.Count;
        if (count == 0)
        {
            CountText.Text = "No tracks in queue";
        }
        else if (count == 1)
        {
            CountText.Text = "1 track in queue";
        }
        else
        {
            CountText.Text = $"{count} tracks in queue";
        }
    }

    private void OnQueueSourceChange()
    {
        SetCountText();
        NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = QueueViewModel.Source.Count > 0;
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

    private async void OpenSpekButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var song = button?.Tag as SongSearchObject;
        if (song == null || song.Source != "local" || !File.Exists(song.Id))  // SongSearchObject.id for local tracks is the file path
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Track does not exist locally", 3);
            return;
        }
        await SpectrogramDialog.OpenSpectrogramDialog(song, dispatcherQueue, this.XamlRoot);
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

        if (IsConverting)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Cannot remove track while queue is running", 3);
            return;
        }

        await QueueViewModel.Remove(selectedSong);
        PreviewPanel.Clear();
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

    private async void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        await QueueViewModel.Clear();
        ShowInfoBar(InfoBarSeverity.Informational, "Queue cleared");
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

    private void ShowInfoBarPermanent(InfoBarSeverity severity, string message, string title = "", string buttonText = "")
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
    private void DispatcherTimer_Tick(object? sender, object? e)
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

    private async Task ConversionTask(IEnumerable<string> selectedSources, string outputSource)
    {
        var songsWithOriginalIndex = QueueViewModel.Source
            .Select((song, originalIndex) => new { Song = song, OriginalIndex = originalIndex });
        var songsToConvert = songsWithOriginalIndex
            .Where(x => selectedSources.Contains(x.Song.Source) && outputSource != x.Song.Source);

        int totalCount = songsToConvert.Count();
        if (totalCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, $"No tracks to {(outputSource == "local" ? "download" : "convert")}", 3);
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
        // Reset back to normal
        ProgressTextButton.Visibility = Visibility.Collapsed;
        CountTextButton.Visibility = Visibility.Visible;
        QueueProgress.Value = 0;

        cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source

        // Clear the conversion results
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        QueueViewModel.ResetConversionResults(); // Clear all badges and results on queue objects
        ViewModel.SuccessCount = ViewModel.WarningCount = ViewModel.ErrorCount = 0; // Reset the counts

        void conversionUpdateCallback(InfoBarSeverity severity, SongSearchObject song, string? location = null)
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
        }

        // For to local output
        var downloadHelper = new DownloadProgressHelper($"Queue to <a href='{directory}'>{directory}</a>", totalCount);
        downloadHelper.ProgressUpdated += (sender, args) =>
        {
            var finalStr = args.DisplayString;
            var progress = args.ProgressValue;
            dispatcherQueue.TryEnqueue(() =>
            {
                QueueProgress.Value = Math.Clamp(progress * 100, 0, 100); ;
                ShowInfoBarPermanent(InfoBarSeverity.Informational, finalStr, title: "Downloading");
            });
        };
        var progress = new Progress<ProgressData>(downloadHelper.UpdateProgressLoop);

        try
        {
            // Infobar notification
            if (outputSource == "local")
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Saving {totalCount} tracks to <a href='{directory}'>{directory}</a>", title: "Download in Progress");
            }
            else
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Converting selected input sources to {GetOutputSource()}", title: "Conversion in Progress");
            }

            // Prepare UI
            DownloadAllButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Collapsed; // Hide buttons
            ConvertStopButton.Visibility = Visibility.Visible; // Show stop button

            // Other initialization
            if (outputSource == "local")
            {
                downloadHelper.StartLoop();
            }
            IsConverting = true;
            int processedCount = 0;
            var token = cancellationTokenSource.Token;

            var downloadThreads = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.ConversionThreads) ?? 1;
            using var semaphore = new SemaphoreSlim(downloadThreads);

            var tasks = songsToConvert.Select(async (x) =>
            {
                var song = x.Song;
                var index = x.OriginalIndex;
                try
                {
                    bool isReleased = false;
                    await semaphore.WaitAsync(token);
                    try
                    {
                        // Set song to running (progress ring will appear)
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            song.IsRunning = true;
                        });

                        // Convert the track and get new song object
                        SongSearchObject? newSongObj = null;
                        string? fileLocation = null;
                        if (outputSource == "local") // Conversion to local, download the track
                        {
                            fileLocation = await ApiHelper.DownloadObject(song, directory, progress, conversionUpdateCallback);
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

                        var tcs = new TaskCompletionSource();
                        // Update the UI with the new song object or set error badge if conversion failed
                        dispatcherQueue.TryEnqueue(async () =>
                        {
                            try
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
                                            queueObj = await QueueViewModel.CreateQueueObject(localSong, song.QueueCounter);
                                            await QueueViewModel.Replace(index, queueObj);
                                        }
                                        else // Can happen when trying to parse opus files
                                        {
                                            queueObj = song; // Set to the initial object so badge is shown below
                                        }
                                    }
                                    else
                                    {
                                        queueObj = await QueueViewModel.CreateQueueObject(newSongObj, song.QueueCounter);
                                        await QueueViewModel.Replace(index, queueObj);
                                    }

                                    if (queueObj != null)
                                    {
                                        if (successSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                                        else if (warningSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 225, 0));
                                        else // Assume failure
                                        {
                                            queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                        }
                                    }
                                }
                                else // Failed conversion, set current object badge to error
                                {
                                    song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                }
                            }
                            finally
                            {
                                tcs.TrySetResult();
                            }
                        });

                        // No need to wait for dispatcher/ui update to complete
                        semaphore.Release();
                        isReleased = true;

                        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            // Exception color code
                            song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0));
                        });
                    }
                    finally
                    {
                        var processCaptured = Interlocked.Increment(ref processedCount); // Increment the processed count
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            song.IsRunning = false; // Set song to not running
                            if (outputSource != "local")
                            {
                                QueueProgress.Value = 100.0 * processCaptured / totalCount; // Update the progress bar
                            }
                        });

                        if (!isReleased)
                        {
                            semaphore.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Handle cancellation if needed
                }
            });

            await Task.WhenAll(tasks);

            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications) ?? false)
            {
                App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
            }
        }
        finally
        {
            // Resetting UI
            dispatcherQueue.TryEnqueue(() =>
            {
                QueueProgress.Value = 0;  // Ensures this happens after currently queued progress changes
            });

            DownloadAllButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Visible; // Show buttons
            ConvertStopText.Text = "Stop"; // Reset stop button text
            ConvertStopButton.Visibility = Visibility.Collapsed; // Hide stop button
            IsConverting = false;

            if (outputSource == "local")
            {
                downloadHelper.StopLoop();
            }

            ForceHideInfoBar();

            await ShowConversionDialog();
        }
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        string outputSource = "local";
        var selectedSources = new List<string> { "deezer", "qobuz", "spotify", "youtube" }; // All sources except local

        await ConversionTask(selectedSources, outputSource);
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

        await ConversionTask(selectedSources, outputSource);
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