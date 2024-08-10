using System.Collections.ObjectModel;
using System.Diagnostics;
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

public delegate void ConversionUpdateCallback(InfoBarSeverity severity, SongSearchObject song); // Callback for conversion updates

public sealed partial class QueuePage : Page
{
    // Create dispatcher queue
    private DispatcherQueue dispatcherQueue;
    private DispatcherTimer dispatcherTimer;
    private CancellationTokenSource cancellationTokenSource;

    // Conversion variables
    private bool isConverting = false;
    private HashSet<string> selectedSources;
    private HashSet<SongSearchObject> successSource, warningSource, errorSource;

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


        // Set conversion variables
        successSource = new HashSet<SongSearchObject>();
        warningSource = new HashSet<SongSearchObject>();
        errorSource = new HashSet<SongSearchObject>();
        selectedSources = new HashSet<string>();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Debug.WriteLine("NAVIGATED AWAY");
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
        if (isConverting) // Ignore the below
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
                ProgressText.Text = "No tracks in queue";
                QueueProgress.Value = 0;
                StartStopButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                var completedCount = QueueViewModel.GetCompletedCount();
                QueueProgress.Value = 100.0 * completedCount / QueueViewModel.Source.Count;
                if (completedCount > 0)
                {
                    ProgressText.Text = (QueueViewModel.IsRunning ? "Running " : "Completed ") + $"{QueueViewModel.GetCompletedCount()} of {QueueViewModel.Source.Count}";
                }

                if (completedCount == QueueViewModel.Source.Count) // If all tracks are completed
                {
                    StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button
                    EnableButtons();
                    App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory)); // Send notification
                }
            }

            NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void InitPreviewPanelButtons()
    {
        var copySourceButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Link), Label = "Copy Source" };
        copySourceButton.Click += (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
            var uri = selectedSong.Source switch
            {
                "spotify" => $"https://open.spotify.com/track/{selectedSong.Id}",
                "deezer" => $"https://www.deezer.com/track/{selectedSong.Id}",
                "youtube" => $"https://www.youtube.com/watch?v={selectedSong.Id}",
                "local" => selectedSong.Id,
                _ => null
            };
            if (uri == null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Failed to copy source to clipboard");
                return;
            }

            Clipboard.CopyToClipboard(uri);
            ShowInfoBar(InfoBarSeverity.Success, "Copied to clipboard");
        };

        var downloadButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Download), Label = "Download" };
        downloadButton.Click += async (sender, e) =>
        {
            var songObj = PreviewPanel.GetSong();
            if (songObj == null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Failed to download track");
                return;
            }

            // Create file name
            var firstArtist = songObj.Artists.Split(",")[0].Trim();
            var isrcStr = !string.IsNullOrWhiteSpace(songObj.Isrc) ? $" [{songObj.Isrc}]" : "";
            var safeFileName = ApiHelper.GetSafeFilename($"{songObj.TrackPosition}. {firstArtist} - {songObj.Title}{isrcStr}.flac");

            // Create a file save picker
            var savePicker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = safeFileName };

            savePicker.FileTypeChoices.Add("FLAC", new List<string> { ".flac" });

            // Retrieve the window handle (HWND) of the current WinUI 3 window
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            // Initialize the file picker with the window handle (HWND)
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

            var file = await savePicker.PickSaveFileAsync();

            if (file != null)
            {
                await ApiHelper.DownloadObject(songObj, file);
            }
        };

        var downloadCoverButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uEE71" }, Label = "Download Cover" };
        downloadCoverButton.Click += async (sender, e) =>
        {
            var bitmapImg = PreviewPanel.GetImage();
            var songObj = PreviewPanel.GetSong();

            // Check if the image is null
            if (bitmapImg == null || songObj == null)
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
                var coverBytes = await new HttpClient().GetByteArrayAsync(bitmapImg.UriSource);
                await File.WriteAllBytesAsync(file.Path, coverBytes);
            }

            //await DeezerApi.DownloadTrack(await DeezerApi.GetTrack(PreviewPanel.GetSong().Id), "E:\\Other Downloads\\test");
        };

        var removeButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Delete), Label = "Remove" };
        removeButton.Click += (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
            if (selectedSong == null)
            {
                return;
            }

            QueueViewModel.Remove(selectedSong);
            PreviewPanel.Clear();
        };

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { copySourceButton, downloadButton, downloadCoverButton, removeButton });
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
        QueueViewModel.RunCommand(directoryInputText, cancellationTokenSource.Token);
        SetPauseUI();

        LocalCommands.AddCommand(commandInputText); // Add the command to the previous command list
        LocalCommands.AddPath(directoryInputText); // Add the path to the previous path list

        await LocalCommands.SaveLatestCommand(commandInputText);
        await LocalCommands.SaveLatestPath(directoryInputText);
        await LocalCommands.SaveCommands();
        await LocalCommands.SavePaths();
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        QueueViewModel.Clear();
        StartStopButton.Visibility = Visibility.Collapsed; // Display start stop
    }

    private void StartStopButton_OnClick(object sender, RoutedEventArgs e)
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
            QueueViewModel.RunCommand(DirectoryInput.Text, cancellationTokenSource.Token);
            SetPauseUI();
        }
    }

    private void SetContinueUI()
    {
        StartStopIcon.Glyph = "\uE768"; // Change the icon to a start icon
        StartStopText.Text = "Continue";
        EnableButtons();
    }

    private void SetPauseUI()
    {
        StartStopIcon.Glyph = "\uE769"; // Change the icon to a pause icon
        StartStopText.Text = "Pause";
        DisableButtons();
    }

    private void EnableButtons()
    {
        CommandButton.IsEnabled = true;
        ConvertDialogOpenButton.IsEnabled = true;
        ClearButton.IsEnabled = true;
    }

    private void DisableButtons()
    {
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

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            PageInfoBar.Content = message;
        });
        dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
        dispatcherTimer.Start();
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object sender, object e)
    {
        PageInfoBar.Opacity = 0;
        (sender as DispatcherTimer).Stop();
        // Set IsOpen to false after 0.25 seconds
        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            dispatcherQueue.TryEnqueue(() =>
            {
                PageInfoBar.IsOpen = false;
            });
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
        ConversionDialog.ShowAsync();
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
        // Loop through and find the total number of queries to process
        int totalCount = 0;
        foreach (var song in QueueViewModel.Source)
        {
            if (selectedSources.Contains(song.Source) && outputSource != song.Source) // If source is selected as input and isn't the same as output
            {
                totalCount++;
            }
        }

        if (totalCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to convert", 3);
            return;
        }

        // Set download location if output is local and ask before download is enabled or download directory is not set
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);

        if (outputSource == "local" && (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory)))
        {
            var openPicker = new Windows.Storage.Pickers.FolderPicker();

            // Retrieve the window handle of the current WinUI 3 window.
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            // Initialize the folder picker with the window handle.
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

            openPicker.SuggestedStartLocation = PickerLocationId.Downloads;
            openPicker.FileTypeFilter.Add("*");

            // Open the picker for the user to pick a folder
            StorageFolder folder = await openPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder); // Save the folder for future access
                directory = folder.Path;
            }
            else // No folder selected
            {
                if (string.IsNullOrWhiteSpace(directory)) // If no directory is set, return
                {
                    ShowInfoBar(InfoBarSeverity.Warning, "No download directory selected", 3);
                    return;
                }
            }
        }

        // Clear preview pane when source is being edited
        PreviewPanel.Clear();

        // Clear queue command running progress
        QueueViewModel.Reset(); // Reset the queue object result strings and index
        cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source

        // Prepare UI
        CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Collapsed; // Hide buttons
        ConvertStopButton.Visibility = Visibility.Visible; // Show stop button
        isConverting = true;

        // Clear the conversion results
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        QueueViewModel.ResetConversionResults(); // Clear all badges and results on queue objects
        ViewModel.SuccessCount = ViewModel.WarningCount = ViewModel.ErrorCount = 0; // Reset the counts

        ConversionUpdateCallback conversionUpdateCallback = (severity, song) =>
        {
            switch (severity)
            {
                case InfoBarSeverity.Success:
                    successSource.Add(song);
                    break;
                case InfoBarSeverity.Warning:
                    warningSource.Add(song);
                    break;
                case InfoBarSeverity.Error:
                    errorSource.Add(song);
                    break;
            }
        };

        // Infobar notification
        ShowInfoBar(InfoBarSeverity.Informational, $"Converting selected input sources to {GetOutputSource()}", 3);

        var ogVal = QueueProgress.Value; // Save the original value of the progress bar
        QueueProgress.Value = 0; // Set to 0


        for (int i = 0; i < QueueViewModel.Source.Count; i++)
        {
            if (cancellationTokenSource.Token.IsCancellationRequested) // If conversion is paused
            {
                break;
            }

            var song = QueueViewModel.Source[i];

            if (!selectedSources.Contains(song.Source) || outputSource == song.Source) // If the source is not selected as input or no conversion needed
            {
                QueueProgress.Value = 100.0 * (i + 1) / totalCount; // Update the progress bar
                continue;
            }

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
                    "deezer" => await DeezerApi.GetDeezerTrack(song, cancellationTokenSource.Token, conversionUpdateCallback),
                    "qobuz" => await QobuzApi.GetQobuzTrack(song, cancellationTokenSource.Token, conversionUpdateCallback),
                    "spotify" => await SpotifyApi.GetSpotifyTrack(song, cancellationTokenSource.Token, conversionUpdateCallback),
                    "youtube" => await YoutubeApi.GetYoutubeTrack(song, cancellationTokenSource.Token, conversionUpdateCallback),
                    _ => throw new Exception("Unspecified output source") // Should never happen
                };
            }

            if (cancellationTokenSource.Token.IsCancellationRequested) // If conversion is paused
            {
                break;
            }

            if (newSongObj != null)
            {
                QueueObject? queueObj;
                if (outputSource == "local")
                {
                    // Get the downloaded song as an object
                    var localSong = LocalExplorerViewModel.ParseFile(fileLocation);

                    if (localSong == null) return; // Skip if song is null

                    // Set song art
                    using var memoryStream = await Task.Run(() => LocalExplorerViewModel.GetAlbumArtMemoryStream(localSong.Id));

                    if (memoryStream != null) // Set album art if available
                    {
                        var bitmapImage = new BitmapImage
                        {
                            DecodePixelHeight = 76, // No need to set height, aspect ratio is automatically handled
                        };
                        await bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream());
                        localSong.LocalBitmapImage = bitmapImage;
                    }

                    queueObj = await QueueViewModel.CreateQueueObject(localSong);
                }
                else
                {
                    queueObj = await QueueViewModel.CreateQueueObject(newSongObj);
                }

                if (queueObj != null)
                {
                    if (successSource.Contains(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                    else if (warningSource.Contains(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 225, 0));

                    QueueViewModel.Replace(i, queueObj);
                }
            }
            else // Failed conversion, set current object badge to error
            {
                song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                QueueViewModel.Replace(i, song);
            }

            QueueProgress.Value = 100.0 * (i + 1) / totalCount; // Update the progress bar
        }

        CommandButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = Visibility.Visible; // Show buttons
        ConvertStopButton.Visibility = Visibility.Collapsed; // Hide stop button
        isConverting = false;
        QueueProgress.Value = ogVal; // Reset the progress bar

        // Show conversion results dialog
        await ShowConversionDialog();
    }

    private void ConvertStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (isConverting) // If currently converting
        {
            cancellationTokenSource.Cancel(); // Cancel the conversion
        }
    }

    private void ConversionTabView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = ConversionTabView.SelectedItem as TabViewItem;
        // Set items source based on selected tab
        if (selectedItem == SuccessTab)
        {
            ConversionListView.ItemsSource = successSource;
            TabInfoBar.Severity = InfoBarSeverity.Success;
            TabInfoBar.Content = "Exact matches found through ISRC identifiers";
            NoConversionText.Visibility = successSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == WarningTab)
        {
            ConversionListView.ItemsSource = warningSource;
            TabInfoBar.Severity = InfoBarSeverity.Warning;
            TabInfoBar.Content = "Attempted matches found through metadata";
            NoConversionText.Visibility = warningSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == ErrorTab)
        {
            ConversionListView.ItemsSource = errorSource;
            TabInfoBar.Severity = InfoBarSeverity.Error;
            TabInfoBar.Content = "No matches found";
            NoConversionText.Visibility = errorSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task ShowConversionDialog()
    {
        ConversionResultsDialog.XamlRoot = this.XamlRoot;
        ConversionTabView.SelectedItem = SuccessTab; // Default to success tab

        // Set the counts
        ViewModel.SuccessCount = successSource.Count;
        ViewModel.WarningCount = warningSource.Count;
        ViewModel.ErrorCount = errorSource.Count;

        // Show the dialog
        await ConversionResultsDialog.ShowAsync();
    }
}