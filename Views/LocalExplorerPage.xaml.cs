﻿using System.Collections.ObjectModel;
using System.Diagnostics;
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

namespace FluentDL.Views;

public sealed partial class LocalExplorerPage : Page
{
    private ObservableCollection<SongSearchObject> originalList;
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;
    private HashSet<string> fileSet;

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
    }

    private void InitPreviewPanelButtons()
    {
        // Initialize preview panel command bar
        var addButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add to queue" };
        addButton.Click += (sender, e) =>
        {
            if (PreviewPanel.GetSong() != null)
            {
                var beforeCount = QueueViewModel.Source.Count;
                QueueViewModel.Add(PreviewPanel.GetSong());
                if (QueueViewModel.Source.Count == beforeCount) // No change
                {
                    ShowInfoBar(InfoBarSeverity.Informational, $"{PreviewPanel.GetSong().Title} already in queue");
                }
                else
                {
                    ShowInfoBar(InfoBarSeverity.Informational, $"{PreviewPanel.GetSong().Title} added to queue");
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
                ShowInfoBar(InfoBarSeverity.Informational, $"{selectedSong.Title} removed from local explorer");
                PreviewPanel.Clear();
            }
        };

        var openButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8A7" }, Label = "Open" };
        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { addButton, removeButton, openButton });
    }

    private void SortOrderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void SortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
            // Get all music files in the folder
            Thread t = new Thread(async () => ProcessFiles(await folder.GetFilesAsync()));
            t.Start();
        }
        else
        {
        }
    }

    private async void UploadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Create a file picker
        FileOpenPicker openPicker = new() { ViewMode = PickerViewMode.List, FileTypeFilter = { ".mp3", ".aac", ".flac", ".wav" }, SuggestedStartLocation = PickerLocationId.MusicLibrary };

        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the file picker with the window handle (HWND).
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Open the picker for the user to pick a file
        IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();

        if (files.Count > 0)
        {
            ShowInfoBar(InfoBarSeverity.Informational, $"Loading {files.Count} files");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No files selected");
        }

        // Create thread to process the files
        Thread t = new Thread(() => ProcessFiles(files));
        t.Start();
    }

    private void ProcessFiles(IReadOnlyList<StorageFile> files)
    {
        if (files.Count > 0)
        {
            foreach (var file in files)
            {
                if (fileSet.Contains(file.Path)) // file.Path is the Id of a song
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
                });

                var memoryStream = LocalExplorerViewModel.GetAlbumArtMemoryStream(song);

                if (memoryStream != null) // Set album art if available
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        var bitmapImage = new BitmapImage();
                        song.LocalBitmapImage = bitmapImage;

                        bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream()).Completed += (info, status) =>
                        {
                            // Refresh the listview to show the album art
                            dispatcher.TryEnqueue(() =>
                            {
                                // Find old index of song object
                                var index = originalList.IndexOf(song);
                                originalList[index] = song;
                                ((ObservableCollection<SongSearchObject>)FileListView.ItemsSource)[index] = song;
                            });
                        };
                    });
                }
            }
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
        await PreviewPanel.Update(selectedSong);
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
                songList = originalList.ToList(); // SongSearchObject
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
                songList = songList.OrderBy(song => song.Rank).ToList();
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
    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2)
    {
        dispatcher.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            PageInfoBar.Content = message;
        });
        dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
        dispatcherTimer.Start();
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
}