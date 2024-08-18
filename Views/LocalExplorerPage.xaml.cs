using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq.Expressions;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using FluentDL.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using FluentDL.Models;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using System.Text.RegularExpressions;
using FluentDL.Helpers;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.Views;

// TODO: notification after clear is a bit broken

public sealed partial class LocalExplorerPage : Page
{
    private ObservableCollection<SongSearchObject> originalList;
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;
    private HashSet<string> fileSet;

    private static HashSet<string> supportedExtensions = new()
    {
        ".aac",
        ".mp4",
        ".m4a",
        ".m4b",
        ".caf",
        ".aax",
        ".aa",
        ".aif",
        ".aiff",
        ".aifc",
        ".dts",
        ".dsd",
        ".dsf",
        ".ac3",
        ".xm",
        ".flac",
        ".gym",
        ".it",
        ".mid",
        ".midi",
        ".ape",
        ".mp1",
        ".mp2",
        ".mp3",
        ".mpc",
        ".mod",
        ".ogg",
        ".oga",
        ".opus",
        ".ofr",
        ".ofs",
        ".psf",
        ".psf1",
        ".psf2",
        ".minipsf",
        ".minipsf1",
        ".minipsf2",
        ".ssf",
        ".minissf",
        ".dsf",
        ".minidsf",
        ".gsf",
        ".minigsf",
        ".qsf",
        ".miniqsf",
        ".s3m",
        ".spc",
        ".tak",
        ".tta",
        ".vqf",
        ".wav",
        ".bwav",
        ".bwf",
        ".vgm",
        ".vgz",
        ".wv",
        ".wma",
        ".asf"
    };

    public LocalExplorerViewModel ViewModel
    {
        get;
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

        FileListView.ItemsSource = new ObservableCollection<SongSearchObject>();
        originalList = new ObservableCollection<SongSearchObject>();
        fileSet = new HashSet<string>();
        InitPreviewPanelButtons();

        // Attach changed event for originalList (when any songs are added or removed from the local explorer)
        originalList.CollectionChanged += (sender, e) =>
        {
            SetResultsAmount(originalList.Count);
            NoItemsText.Visibility = originalList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearButton.IsEnabled = originalList.Count > 0;
            ClearButton.IsEnabled = QueueViewModel.Source.Count > 0;
        };

        // Set first
        SetResultsAmount(0);
        ClearButton.IsEnabled = originalList.Count > 0;
    }

    private void InitPreviewPanelButtons()
    {
        // Initialize preview panel command bar
        var addButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add to queue" };
        addButton.Click += (sender, e) =>
        {
            var song = PreviewPanel.GetSong();
            if (song != null)
            {
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
            }
        };

        var removeButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Delete), Label = "Remove" };
        removeButton.Click += (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
            if (selectedSong != null)
            {
                ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource).Remove(selectedSong);
                originalList.Remove(selectedSong);
                fileSet.Remove(selectedSong.Id);
                ShowInfoBar(InfoBarSeverity.Success, $"{selectedSong.Title} removed from local explorer");
                PreviewPanel.Clear();
            }
        };

        var editButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Edit), Label = "Edit" };
        editButton.Click += (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
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
        };

        var openButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8A7" }, Label = "Open" };
        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { addButton, removeButton, editButton, openButton });
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e) // Navigated to page
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)FileListView.SelectedItem;
        if (selectedSong == null)
        {
            return;
        }

        PreviewPanel.Show();
        await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
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
                if (fileSet.Contains(file.Path) || !supportedExtensions.Contains(file.FileType.ToLower())) // file.Path is the Id of a song
                {
                    continue;
                }

                var song = LocalExplorerViewModel.ParseFile(file.Path);

                if (song == null) continue; // Skip if song is null

                dispatcher.TryEnqueue(() =>
                {
                    // Add song to both the original list and the listview
                    originalList.Add(song);
                    ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource).Add(song);
                    fileSet.Add(song.Id);
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

        if (FileListView.ItemsSource == null)
        {
            return;
        }

        var songList = ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource).ToList();

        switch (selectedIndex)
        {
            case 0:
                songList = originalList.ToList(); // Default order
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

        FileListView.ItemsSource = new ObservableCollection<SongSearchObject>(songList);
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
            originalList.Clear();
            ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource).Clear();
            fileSet.Clear();
        });
    }

    private void AddToQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (originalList.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to add");
            return;
        }

        var beforeCount = QueueViewModel.Source.Count;

        foreach (var song in originalList)
        {
            QueueViewModel.Add(song);
        }

        var addedCount = QueueViewModel.Source.Count - beforeCount;

        if (addedCount < originalList.Count)
        {
            ShowInfoBar(InfoBarSeverity.Informational, $"Added {addedCount} tracks to queue (duplicates ignored)");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"Added {addedCount} tracks to queue");
        }
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
        if (!fileSet.Contains(path))
        {
            return;
        }

        // Get the new song
        var song = LocalExplorerViewModel.ParseFile(path);

        if (song == null) return; // Skip if song is null

        // Set song art
        using var memoryStream = await Task.Run(() => LocalExplorerViewModel.GetAlbumArtMemoryStream(song.Id));

        if (memoryStream != null) // Set album art if available
        {
            var bitmapImage = new BitmapImage
            {
                DecodePixelHeight = 76, // No need to set height, aspect ratio is automatically handled
            };
            await bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream());
            song.LocalBitmapImage = bitmapImage;
        }

        // Update the song in the listview
        var index = originalList.IndexOf(originalList.First(s => s.Id == song.Id));
        originalList[index] = song;

        var listViewSource = (ObservableCollection<SongSearchObject>)FileListView.ItemsSource;
        index = listViewSource.IndexOf(listViewSource.First(s => s.Id == song.Id));
        listViewSource[index] = song;

        PreviewPanel.Show();
        // Update the preview panel
        await PreviewPanel.Update(song, LocalExplorerViewModel.GetMetadataObject(song.Id));

        ShowInfoBar(InfoBarSeverity.Success, $"Saved metadata for <a href='{song.Id}'>{song.Title}</a>");
    }

    private void AddQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the Tag property
        var song = button?.Tag as SongSearchObject;

        var beforeCount = QueueViewModel.Source.Count;
        if (song == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No song selected"); // Technically shouldn't happen
            return;
        }

        QueueViewModel.Add(song);
        if (QueueViewModel.Source.Count == beforeCount) // No change
        {
            ShowInfoBar(InfoBarSeverity.Warning, $"<a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> already in queue");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"<a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> added to queue");
        }
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
}