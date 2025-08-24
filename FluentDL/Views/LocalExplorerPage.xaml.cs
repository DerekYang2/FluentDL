using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using TagLib.Id3v2;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using WinUIEx;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.Views;

// TODO: notification after clear is a bit broken

public class LocalConversionResult
{
    public SongSearchObject Song;
    public string? NewPath;

    public LocalConversionResult(SongSearchObject song, string? newPath)
    {
        this.Song = song;
        this.NewPath = newPath;
    }
}

public sealed partial class LocalExplorerPage : Page
{
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;
    private CancellationTokenSource? cancellationTokenSource = null;

    public LocalExplorerViewModel ViewModel
    {
        get;
    }

    public ObservableCollection<LocalConversionResult> ConversionResults
    {
        get;
        set;
    }


    public LocalExplorerPage()
    {
        ViewModel = App.GetService<LocalExplorerViewModel>();
        InitializeComponent();

        dispatcher = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;

        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;

        OutputComboBox.ItemsSource = new List<string>
        {
            ".flac",
            ".mp3",
            ".m4a (AAC)",
            ".m4a (ALAC)",
            ".ogg (Vorbis)",
            ".ogg (Opus)",
        };

        FileListView.ItemsSource = LocalExplorerViewModel.Source;
        ConversionResults = new ObservableCollection<LocalConversionResult>();
        InitPreviewPanelButtons();
        InitializeAnimations();

        // Attach changed event for originalList (when any songs are added or removed from the local explorer)
        LocalExplorerViewModel.OriginalList.CollectionChanged += ListChangeDelegate;
        ListChangeDelegate(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));  // Call once upon construction

        ConversionResults.CollectionChanged += (sender, e) =>
        {
            if (ConversionResults.Count == 0)
            {
                NoConversionText.Visibility = Visibility.Visible;
            }
            else
            {
                NoConversionText.Visibility = Visibility.Collapsed;
            }
        };

        // Set first
        SetResultsAmount(0);
        ClearButton.IsEnabled = LocalExplorerViewModel.OriginalList.Count > 0;

        // Set on load
        this.Loaded += LocalExplorerPage_Loaded;
    }

    private void ListChangeDelegate(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SetResultsAmount(LocalExplorerViewModel.OriginalList.Count);
        NoItemsText.Visibility = LocalExplorerViewModel.OriginalList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = LocalExplorerViewModel.OriginalList.Count > 0;
    }

    private async void LocalExplorerPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void InitializeAnimations()
    {
        AnimationHelper.AttachSpringUpAnimation(UploadImageButton, UploadImageIcon);
        AnimationHelper.AttachScaleAnimation(UploadButton, UploadButtonIcon);
        AnimationHelper.AttachSpringUpAnimation(UploadFileButton, UploadFileButtonIcon);
        AnimationHelper.AttachScaleAnimation(ClearButton, ClearButtonIcon);
        AnimationHelper.AttachScaleAnimation(AddToQueueButton, ResultsIcon);
        AnimationHelper.AttachScaleAnimation(ConvertDialogOpenButton, ConvertDialogOpenIcon);
        AnimationHelper.AttachScaleAnimation(SelectOutputButton, SelectOutputIcon);
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e) // Navigated to page
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)FileListView.SelectedItem;
        if (selectedSong != null)
        {
            PreviewPanel.Show();
            await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
        }

        // Initialize the settings for shortcut buttons
        await ViewModel.InitializeAsync();

        // Refresh the listview items 
        SortListView();
    }

    private void InitPreviewPanelButtons()
    {
        // Initialize preview panel command bar
        var addButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add to queue" };
        addButton.Click += (sender, e) => AddSongToQueue(PreviewPanel.GetSong());

        var editButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Edit), Label = "Edit" };
        editButton.Click += (sender, e) => OpenMetadataDialog(PreviewPanel.GetSong());

        var openSpekButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE9D2" }, Label = "Analyze" };
        openSpekButton.Click += (sender, e) => { SpekRunner.RunSpek(PreviewPanel.GetSong()?.Id); };

        var openButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8DA" }, Label = "Open" };
        openButton.Click += (sender, e) => OpenSongInExplorer(PreviewPanel.GetSong());

        // Initialize animations
        AnimationHelper.AttachScaleAnimation(addButton);
        AnimationHelper.AttachScaleAnimation(editButton);
        AnimationHelper.AttachSpringUpAnimation(openSpekButton);
        AnimationHelper.AttachSpringUpAnimation(openButton);

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { addButton, editButton, openSpekButton, openButton });
    }

    private void AddToQueueShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;
        AddSongToQueue(song);
    }

    private void EditButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;
        OpenMetadataDialog(song);
    }

    private void OpenInBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;
        OpenSongInExplorer(song);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) // Navigated away from page
    {
        PreviewPanel.Clear(); // Clear preview 
    }

    public void SetResultsAmount(int amount)
    {
        if (amount == 0)
        {
            ResultsText.Text = "No results";
            ResultsIcon.Visibility = Visibility.Collapsed; // Hide plus icon
        }
        else
        {
            ResultsText.Text = "Add " + amount + " to queue";
            ResultsIcon.Visibility = Visibility.Visible; // Show plus icon
        }
    }

    private void SortOrderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SortListView();
    }

    private void SortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SortListView();
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
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
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder);
            ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Scanning <a href='{folder.Path}'>{folder.Name}</a> for audio files", "Upload");
            InfobarProgress.Visibility = Visibility.Visible;
            // Get all music files in the folder
            Thread t = new Thread(async () =>
            {
                ProcessFiles(await folder.GetFilesAsync());
                dispatcher.TryEnqueue(() =>
                {
                    InfobarProgress.Visibility = Visibility.Collapsed; // Hide progress bar
                });
            });
            t.Start();
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No folder selected");
        }

        SortListView();
    }

    private async void UploadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Create a file picker
        FileOpenPicker openPicker = new()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter =
            {
                ".aac",
                ".mp4",
                ".m4a",
                ".flac",
                ".mp3",
                ".ogg",
                ".wav",
            },
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };

        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the file picker with the window handle (HWND).
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Open the picker for the user to pick a file
        IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();

        if (files.Count > 0)
        {
            if (files.Count == 1) // Singular
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Loading {files.Count} local track", "Upload");
            }
            else // Plural
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Loading {files.Count} local tracks", "Upload");
            }

            InfobarProgress.Visibility = Visibility.Visible;
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No files selected");
        }

        // Create thread to process the files
        Thread t = new Thread(() =>
        {
            ProcessFiles(files);
            dispatcher.TryEnqueue(() =>
            {
                InfobarProgress.Visibility = Visibility.Collapsed; // Hide progress bar
            });
        });
        t.Start();

        SortListView();
    }

    private async void UploadImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Create a file picker
        FileOpenPicker openPicker = new() { ViewMode = PickerViewMode.Thumbnail, FileTypeFilter = { ".jpg", ".jpeg", ".png", }, SuggestedStartLocation = PickerLocationId.Downloads };
        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        // Initialize the file picker with the window handle (HWND).
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);
        // Open the picker for the user to pick a file
        var storageFile = await openPicker.PickSingleFileAsync();

        if (storageFile != null)
        {
            ViewModel.SetImagePath(storageFile.Path);
            CoverArtTextBox.Text = storageFile.Path;
        }
    }

    private void ProcessFiles(IReadOnlyList<StorageFile> files)
    {
        if (files.Count > 0)
        {
            // Start loading
            dispatcher.TryEnqueue(() =>
            {
                LoadProgress.IsIndeterminate = true;
                // Disable certain buttons
                ClearButton.IsEnabled = false;
                SortComboBox.IsEnabled = false;
                SortOrderComboBox.IsEnabled = false;
            });

            int addedCount = 0;
            foreach (var file in files)
            {
                if (LocalExplorerViewModel.ContainsFile(file.Path) || !LocalExplorerViewModel.SupportedExtensions.Contains(file.FileType.ToLower())) // file.Path is the Id of a song
                {
                    continue;
                }

                var song = LocalExplorerViewModel.ParseFile(file.Path);

                if (song == null) continue; // Skip if song is null

                dispatcher.TryEnqueue(() =>
                {
                    // Add song to both the original list and the listview
                    LocalExplorerViewModel.AddSong(song);
                    addedCount++;
                });

                var memoryStream = LocalExplorerViewModel.GetAlbumArtMemoryStream(song.Id);

                if (memoryStream != null) // Set album art if available
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        var bitmapImage = new BitmapImage
                        {
                            // No need to set height, aspect ratio is automatically handled
                            DecodePixelHeight = 76,
                        };

                        song.LocalBitmapImage = bitmapImage;
                        bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream()).Completed += (info, status) =>
                        {
                            // Refresh the listview to show the album art
                            var index = ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource).IndexOf(song);
                            ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource)[index] = song;
                            memoryStream.Dispose();
                        };
                    });
                }
            }

            // End loading
            dispatcher.TryEnqueue(() =>
            {
                LoadProgress.IsIndeterminate = false;
                // Re-enable buttons
                ClearButton.IsEnabled = true;
                SortComboBox.IsEnabled = true;
                SortOrderComboBox.IsEnabled = true;
                // Infobar
                if (addedCount < files.Count)
                {
                    ShowInfoBar(InfoBarSeverity.Informational, $"Added {addedCount} tracks to local explorer (duplicates ignored)");
                }
                else
                {
                    ShowInfoBar(InfoBarSeverity.Success, $"Added {addedCount} tracks to local explorer");
                }
            });
        }
    }

    private async void FileListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)FileListView.SelectedItem;
        if (selectedSong == null)
        {
            PreviewPanel.Clear();
            return;
        }

        PreviewPanel.Show();
        await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
    }

    private void SortListView()
    {
        var selectedIndex = SortComboBox.SelectedIndex;
        var isAscending = SortOrderComboBox.SelectedIndex == 0;

        if (LocalExplorerViewModel.Source == null)
        {
            return;
        }

        var songList = LocalExplorerViewModel.Source.ToList();

        switch (selectedIndex)
        {
            case 0:
                songList = LocalExplorerViewModel.OriginalList.ToList(); // Default order
                break;
            case 1:
                songList = songList.OrderBy(song => song.Title).ToList();
                break;
            case 2:
                songList = songList.OrderBy(song => song.Artists).ToList();
                break;
            case 3:
                songList = songList.OrderBy(song => song.ReleaseDate).ToList();
                break;
            case 4:
                songList = songList.OrderBy(song => Path.GetFileName(song.Id)).ToList();
                break;
            case 5:
                songList = songList.OrderBy(song => File.GetCreationTime(song.Id)).ToList();
                break;
            default:
                break;
        }

        if (!isAscending)
        {
            songList.Reverse();
        }

        LocalExplorerViewModel.Source = new ObservableCollection<SongSearchObject>(songList);
        // Refresh list view
        FileListView.ItemsSource = LocalExplorerViewModel.Source;
    }

    // Infobar related methods
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

    private void ShowInfoBarPermanent(InfoBarSeverity severity, string message, string title = "")
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
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>    {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
        });
    }

    private void dispatcherTimer_Tick(object sender, object e)
    {
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

    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clear();
        ShowInfoBar(InfoBarSeverity.Success, "Local explorer cleared");
    }

    public void Clear()
    {
        dispatcher.TryEnqueue(() =>
        {
            PreviewPanel.Clear();
            LocalExplorerViewModel.ClearSongs();
        });
    }

    private async void AddToQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (LocalExplorerViewModel.OriginalList.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to add");
            return;
        }

        var beforeCount = QueueViewModel.Source.Count;

        foreach (var song in LocalExplorerViewModel.OriginalList)
        {
            QueueViewModel.Add(song);
        }

        var addedCount = QueueViewModel.Source.Count - beforeCount;

        if (addedCount < LocalExplorerViewModel.OriginalList.Count)
        {
            ShowInfoBar(InfoBarSeverity.Informational, $"Added {addedCount} tracks to queue (duplicates ignored)");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"Added {addedCount} tracks to queue");
        }

        await QueueViewModel.SaveQueue();
    }

    private async void MetadataDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Stop media player
        PreviewPanel.ClearMediaPlayerSource();

        ViewModel.SetImagePath(CoverArtTextBox.Text);
        await ViewModel.SaveMetadata();
        // Find the song and update it in the listview + preview panel
        var path = ViewModel.GetCurrentEditPath();

        // Check that path is in fileSet
        if (!LocalExplorerViewModel.ContainsFile(path))
        {
            return;
        }

        // Get the new song
        var song = LocalExplorerViewModel.ParseFile(path);

        if (song == null) return; // Skip if song is null

        // Set song art
        song.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(song.Id);
        
        // Update the song in the listview
        var index = LocalExplorerViewModel.OriginalList.IndexOf(LocalExplorerViewModel.OriginalList.First(s => s.Id == song.Id));
        LocalExplorerViewModel.OriginalList[index] = song;

        var listViewSource = (ObservableCollection<SongSearchObject>)FileListView.ItemsSource;
        index = listViewSource.IndexOf(listViewSource.First(s => s.Id == song.Id));
        listViewSource[index] = song;

        PreviewPanel.Show();
        // Update the preview panel
        await PreviewPanel.Update(song, LocalExplorerViewModel.GetMetadataObject(song.Id));

        ShowInfoBar(InfoBarSeverity.Success, $"Saved metadata for <a href='{song.Id}'>{song.Title}</a>");
    }

    private void MetadataDialog_OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
    }

    private void ComboBox_OnDropDownOpened(object? sender, object e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox == null || comboBox.SelectedItem == null)
        {
            return;
        }

        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            comboBox.PlaceholderText = item.Content.ToString();
        }

        if (comboBox.SelectedItem is SortObject sortObject)
        {
            comboBox.PlaceholderText = sortObject.Text + sortObject.Highlight;
        }
    }

    private void OpenMetadataDialog(SongSearchObject? selectedSong)
    {
        if (selectedSong != null)
        {
            MetadataDialog.XamlRoot = this.XamlRoot;
            dispatcher.TryEnqueue(() =>
            {
                ViewModel.SetUpdateObject(selectedSong);
                MetadataTable.ItemsSource = ViewModel.CurrentMetadataList; // Fill table with the metadata list 
                CoverArtTextBox.Text = ViewModel.GetCurrentImagePath() ?? "";
                MetadataDialog.ShowAsync();
            });
        }
    }

    private async void AddSongToQueue(SongSearchObject? song)
    {
        if (song == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No song selected"); // Technically shouldn't happen
            return;
        }

        var beforeCount = QueueViewModel.Source.Count;

        QueueViewModel.Add(song);
        if (QueueViewModel.Source.Count == beforeCount) // No change
        {
            ShowInfoBar(InfoBarSeverity.Warning, $"<a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> already in queue");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"<a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> added to queue");
        }

        await QueueViewModel.SaveQueue();
    }

    private static void OpenSongInExplorer(SongSearchObject? song)
    {
        if (song != null)
        {
            var argument = $"/select, \"{song.Id}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
    }

    // Convert dialog related methods ------------------------------------------------

    private async void ConvertDialogOpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConversionDialog.XamlRoot = this.XamlRoot;
        if (ConversionInfobarProgress.Visibility == Visibility.Collapsed) // If no conversion is in progress
        {
            // Set to default message
            ConversionInfobar.Severity = InfoBarSeverity.Informational;
            UrlParser.ParseTextBlock(ConversionInfobarText, "Convert tracks in Local Explorer to new audio formats");
            OutputComboBox.SelectedIndex = -1; // Reset combobox
            // Conversion list is not cleared as user may refer to conversions later even after closing dialog
        }

        await ConversionDialog.ShowAsync();
    }

    private bool shouldClose;

    private async void ConversionDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        shouldClose = false;
        var selectedIndex = OutputComboBox.SelectedIndex;
        var outputFormat = OutputComboBox.SelectedItem as string;
        var outputDirectory = OutputTextBox.Text;

        // Handle input issues
        if (outputFormat == null)
        {
            ConversionInfobar.Severity = InfoBarSeverity.Warning;
            UrlParser.ParseTextBlock(ConversionInfobarText, "No output format selected");
            return;
        }

        if (!(await Task.Run(() => Directory.Exists(outputDirectory))))
        {
            ConversionInfobar.Severity = InfoBarSeverity.Warning;
            UrlParser.ParseTextBlock(ConversionInfobarText, "Output directory does not exist");
            return;
        }

        // Get the subsetting index
        var subsettingIndex = SubsettingComboBox.SelectedIndex;

        // Set infobar message to running
        ConversionInfobar.Severity = InfoBarSeverity.Informational;
        UrlParser.ParseTextBlock(ConversionInfobarText, $"Converting <b>0 of {LocalExplorerViewModel.OriginalList.Count}</b> tracks to {outputFormat}");
        ConversionInfobarProgress.Visibility = Visibility.Visible;

        // Disable conversion button and hide conversion settings
        ConversionDialog.IsPrimaryButtonEnabled = false;
        ConversionSettingStackPanel.Visibility = Visibility.Collapsed;

        // End conversion lambda
        var EndConversionLambda = new Action(() => dispatcher.TryEnqueue(() =>
        {
            // Unset infobar message
            ConversionInfobarProgress.Visibility = Visibility.Collapsed;
            ConversionInfobar.Severity = InfoBarSeverity.Success;
            UrlParser.ParseTextBlock(ConversionInfobarText, "Conversion complete");

            // Enable conversion button and show conversion settings
            ConversionDialog.IsPrimaryButtonEnabled = true;
            ConversionSettingStackPanel.Visibility = Visibility.Visible;
        }));

        // Cancellation token
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        // Start the conversion
        ConversionListView.ItemsSource = ConversionResults;
        ConversionResults.Clear();


        var threadCount = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.AudioConversionThreads) ?? 1;

        int index = 0;
        int completedCount = 0;

        for (int threadNum = 0; threadNum < threadCount; threadNum++)
        {
            Thread t = new Thread(() =>
            {
                int i;
                do
                {
                    i = Interlocked.Increment(ref index) - 1;
                    if (i >= LocalExplorerViewModel.OriginalList.Count || token.IsCancellationRequested)
                        break;

                    var song = LocalExplorerViewModel.OriginalList[i];
                    var resultPath = selectedIndex switch
                    {
                        0 => FFmpegRunner.CreateFlac(song.Id, outputDirectory),
                        1 => CreateMp3Helper(song, outputDirectory, subsettingIndex),
                        2 => CreateAacHelper(song, outputDirectory, subsettingIndex),
                        3 => FFmpegRunner.CreateAlac(song.Id, outputDirectory),
                        4 => CreateVorbisHelper(song, outputDirectory, subsettingIndex),
                        5 => CreateOpusHelper(song, outputDirectory, subsettingIndex),
                        _ => null,
                    };

                    if (token.IsCancellationRequested)
                        break;

                    dispatcher.TryEnqueue(() =>
                    {
                        ConversionResults.Add(new LocalConversionResult(song, resultPath));
                        // Update infobar converting message
                        UrlParser.ParseTextBlock(ConversionInfobarText, $"Converting <b>{ConversionResults.Count} of {LocalExplorerViewModel.OriginalList.Count}</b> tracks to {outputFormat}");
                    });

                    var countCatpured = Interlocked.Increment(ref completedCount);
                    if (countCatpured == LocalExplorerViewModel.OriginalList.Count && !token.IsCancellationRequested)
                    {
                        EndConversionLambda();
                        break;
                    }
                } while (i + 1 < LocalExplorerViewModel.OriginalList.Count);
            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }
    }

    private void ConversionDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        shouldClose = false;
        cancellationTokenSource?.Cancel();
        // Display end conversion
        dispatcher.TryEnqueue(() =>
        {
            // Unset infobar message
            ConversionInfobarProgress.Visibility = Visibility.Collapsed;
            ConversionInfobar.Severity = InfoBarSeverity.Informational;
            UrlParser.ParseTextBlock(ConversionInfobarText, "Conversion cancelled");

            // Enable conversion button and show conversion settings
            ConversionDialog.IsPrimaryButtonEnabled = true;
            ConversionSettingStackPanel.Visibility = Visibility.Visible;
        });
    }

    private void OpenConversionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var result = button?.Tag as LocalConversionResult;
        if (File.Exists(result?.NewPath)) // Open the file if it exists
        {
            var argument = $"/select, \"{result.NewPath}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
    }

    private void ConversionDialog_OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        shouldClose = true;
    }

    private void ConversionDialog_OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = !shouldClose;
    }

    private void OutputComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var MP3Subsettings = new List<string>
        {
            "Variable bit rate (VBR)",
            "128 kbps",
            "192 kbps",
            "256 kbps",
            "320 kbps",
        };

        var AacSubsettings = new List<string> { "128 kbps", "192 kbps", "256 kbps" };

        var VorbisSubsettings = new List<string>
        {
            "VBR Medium Quality",
            "VBR High Quality",
            "VBR Max Quality",
            "128 kbps",
            "192 kbps",
            "256 kbps"
        };
        var OpusSubsettings = new List<string> { "96 kbps", "128 kbps", "192 kbps" };

        var selectedIndex = OutputComboBox.SelectedIndex;

        SubsettingComboBox.ItemsSource = selectedIndex switch
        {
            1 => MP3Subsettings,
            2 => AacSubsettings,
            4 => VorbisSubsettings,
            5 => OpusSubsettings,
            _ => new List<string>(),
        };

        SubsettingHeader.Text = selectedIndex switch
        {
            1 => "Bitrate",
            2 => "Bitrate (CBR)",
            4 => "Bitrate",
            5 => "Bitrate (VBR)",
            _ => "",
        };

        // If empty, hide the subsetting combobox
        if (SubsettingComboBox.ItemsSource as List<string> is { Count: 0 })
        {
            SubsettingComboBox.Visibility = Visibility.Collapsed;
            SubsettingHeader.Visibility = Visibility.Collapsed;
        }
        else
        {
            SubsettingComboBox.Visibility = Visibility.Visible;
            SubsettingHeader.Visibility = Visibility.Visible;
            SubsettingComboBox.SelectedIndex = 0;
        }
    }

    private string? CreateMp3Helper(SongSearchObject song, string? outputDirectory, int subsettingIndex)
    {
        return subsettingIndex switch
        {
            0 => FFmpegRunner.CreateMp3(song.Id, outputDirectory),
            1 => FFmpegRunner.CreateMp3(song.Id, 128, outputDirectory),
            2 => FFmpegRunner.CreateMp3(song.Id, 192, outputDirectory),
            3 => FFmpegRunner.CreateMp3(song.Id, 256, outputDirectory),
            4 => FFmpegRunner.CreateMp3(song.Id, 320, outputDirectory),
            _ => string.Empty,
        };
    }

    private string? CreateAacHelper(SongSearchObject song, string? outputDirectory, int subsettingIndex)
    {
        return subsettingIndex switch
        {
            0 => FFmpegRunner.CreateAac(song.Id, 128, outputDirectory),
            1 => FFmpegRunner.CreateAac(song.Id, 192, outputDirectory),
            2 => FFmpegRunner.CreateAac(song.Id, 256, outputDirectory),
            _ => string.Empty,
        };
    }

    private string? CreateVorbisHelper(SongSearchObject song, string? outputDirectory, int subsettingIndex)
    {
        return subsettingIndex switch
        {
            0 => FFmpegRunner.CreateVorbisVBR(song.Id, 5, outputDirectory),
            1 => FFmpegRunner.CreateVorbisVBR(song.Id, 8, outputDirectory),
            2 => FFmpegRunner.CreateVorbisVBR(song.Id, 10, outputDirectory),
            3 => FFmpegRunner.CreateVorbis(song.Id, 128, outputDirectory),
            4 => FFmpegRunner.CreateVorbis(song.Id, 192, outputDirectory),
            5 => FFmpegRunner.CreateVorbis(song.Id, 256, outputDirectory),
            _ => string.Empty,
        };
    }

    private string? CreateOpusHelper(SongSearchObject song, string? outputDirectory, int subsettingIndex)
    {
        return subsettingIndex switch
        {
            0 => FFmpegRunner.CreateOpus(song.Id, 96, outputDirectory),
            1 => FFmpegRunner.CreateOpus(song.Id, 128, outputDirectory),
            2 => FFmpegRunner.CreateOpus(song.Id, 192, outputDirectory),
            _ => string.Empty,
        };
    }

    private async void SelectOutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        var folder = await StoragePickerHelper.PickFolderAsync(PickerLocationId.MusicLibrary);
        OutputTextBox.Text = folder?.Path ?? "";
    }
}