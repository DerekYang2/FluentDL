using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;
using FluentDL.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using FluentDL.Contracts.Services;
using FluentDL.Models;
using Microsoft.UI.Dispatching;
using YoutubeExplode.Search;
using Microsoft.UI.Xaml;

namespace FluentDL.Views;
// TODO: loading for search

/*
 *                        <DataTemplate x:DataType="SortObject">
       <StackPanel Orientation="Horizontal">
           <TextBlock Text="{Binding Text}" Foreground="White"/>
           <TextBlock Text="{Binding Highlight}" Foreground="Green"/>
       </StackPanel>
   </DataTemplate>
 */
public class SortObject
{
    public string Text
    {
        get;
        set;
    }

    public string Highlight
    {
        get;
        set;
    }

    public SortObject(string text, string highlight)
    {
        Text = text;
        Highlight = highlight;
    }

    public SortObject()
    {
    }
}

public class TrackDetail
{
    public string Label
    {
        get;
        set;
    }

    public string Value
    {
        get;
        set;
    }
}

public sealed partial class Search : Page
{
    private TerminalSubprocess _terminalSubprocess;
    private ObservableCollection<SongSearchObject> originalList;
    private SpotifyApi spotifyApi;
    private ObservableCollection<SongSearchObject> failedSpotifySongs;
    private List<VideoSearchResult> youtubeAlternateList;
    private bool failDialogOpen = false;
    private CancellationTokenSource cancellationTokenSource;
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;

    public SearchViewModel ViewModel
    {
        get;
    }

    public Search()
    {
        ViewModel = App.GetService<SearchViewModel>();
        InitializeComponent();
        dispatcher = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;

        CustomListView.ItemsSource = new ObservableCollection<SongSearchObject>();
        AttachCollectionChangedEvent((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);
        originalList = new ObservableCollection<SongSearchObject>();
        SetResultsAmount(0);
        _terminalSubprocess = new TerminalSubprocess();
        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;
        InitPreviewPanelButtons();

        youtubeAlternateList = new List<VideoSearchResult>();

        StopSearchButton.Visibility = Visibility.Collapsed;

        // Initialize spotify failed results collection
        failedSpotifySongs = new ObservableCollection<SongSearchObject>();
        FailedResultsButton.Visibility = Visibility.Collapsed;
        failedSpotifySongs.CollectionChanged += (sender, e) =>
        {
            if (failedSpotifySongs.Count > 0)
            {
                FailedResultsButton.Visibility = Visibility.Visible;
                FailedResultsText.Text = failedSpotifySongs.Count + " failed";
            }
            else
            {
                FailedResultsButton.Visibility = Visibility.Collapsed;
            }
        };
        cancellationTokenSource = new CancellationTokenSource();
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
                    ShowInfoBar(InfoBarSeverity.Warning, $"{PreviewPanel.GetSong().Title} already in queue");
                }
                else
                {
                    ShowInfoBar(InfoBarSeverity.Success, $"{PreviewPanel.GetSong().Title} added to queue");
                }
            }
        };

        var shareButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Share), Label = "Share" };
        var openButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8A7" }, Label = "Open" };
        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { addButton, shareButton, openButton });
    }

    private void AttachCollectionChangedEvent(ObservableCollection<SongSearchObject> collection)
    {
        collection.CollectionChanged += (sender, e) =>
        {
            SetResultsAmount(CustomListView.Items.Count);
        };
    }

    private void DisableSearches()
    {
        SearchBox.IsEnabled = false;
        ShowDialogButton.IsEnabled = false;
        SortComboBox.Visibility = Visibility.Collapsed;
        SortOrderComboBox.Visibility = Visibility.Collapsed;
    }

    private void EnableSearches()
    {
        SearchBox.IsEnabled = true;
        ShowDialogButton.IsEnabled = true;
        SortComboBox.Visibility = Visibility.Visible;
        SortOrderComboBox.Visibility = Visibility.Visible;
    }

    public void SetResultsAmount(int amount)
    {
        if (amount == 0)
        {
            ResultsText.Text = "No results";
            ResultsIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ResultsText.Text = "Add " + amount + " to queue";
            ResultsIcon.Visibility = Visibility.Visible;
        }
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
        await PreviewPanel.Update(selectedSong);
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SortCustomListView();
    }

    private void SortOrder_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SortCustomListView();
    }

    private void SortCustomListView()
    {
        var selectedIndex = SortComboBox.SelectedIndex;
        var isAscending = SortOrderComboBox.SelectedIndex == 0;

        if (CustomListView.ItemsSource == null)
        {
            return;
        }

        var songList = ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).ToList();

        switch (selectedIndex)
        {
            case 0:
                songList = originalList.ToList(); // <SongSearchObject
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

        CustomListView.ItemsSource = new ObservableCollection<SongSearchObject>(songList);
        AttachCollectionChangedEvent((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);
    }

    // Advanced search
    private async void SearchDialogClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var artistName = artistNameInput.Text.Trim();
        var trackName = trackNameInput.Text.Trim();
        var albumName = albumNameInput.Text.Trim();

        if (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            return;
        }

        SearchProgress.IsIndeterminate = true;
        NoSearchResults.Visibility = Visibility.Collapsed; // Hide the message for now
        StopSearchButton.Visibility = Visibility.Visible; // Make stop button visible
        SearchBox.Text = ""; // Clear regular search box query
        DisableSearches();
        cancellationTokenSource = new CancellationTokenSource(); // Reset the cancel token

        // Set the collection as the ItemsSource for the ListView
        await FluentDL.Services.DeezerApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, cancellationTokenSource.Token);
        originalList = new ObservableCollection<SongSearchObject>((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);

        SetResultsAmount(originalList.Count);
        SortCustomListView();
        SetNoSearchResults(); // Call again as actual check 
        SearchProgress.IsIndeterminate = false;
        StopSearchButton.Visibility = Visibility.Collapsed;
        EnableSearches();
    }

    private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var generalQuery = SearchBox.Text.Trim();
        if (generalQuery.Length == 0)
        {
            return;
        }

        SearchProgress.IsIndeterminate = true;
        StopSearchButton.Visibility = Visibility.Visible;
        NoSearchResults.Visibility = Visibility.Collapsed; // Hide the message for now
        DisableSearches();
        cancellationTokenSource = new CancellationTokenSource(); // Reset the cancel token

        // Check if query is a spotify playlist link
        // Format of links https://open.spotify.com/playlist/{id}?
        // OR https://open.spotify.com/playlist/{id}?...

        if (generalQuery.StartsWith("https://open.spotify.com/"))
        {
            await SpotifyApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token);
            /*
            var playlistId = generalQuery.Split("/").Last();
            //Spotify to deezer conversion
            await LoadSpotifyPlaylist(playlistId, cancellationTokenSource.Token);
            */
        }
        else if (generalQuery.StartsWith("https://deezer.page.link") || System.Text.RegularExpressions.Regex.IsMatch(generalQuery, @"https://www\.deezer\.com(/[^/]+)?/(track|album|playlist)/.*"))
        {
            await DeezerApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token);
        }
        else if (generalQuery.StartsWith("https://www.qobuz.com/") || generalQuery.StartsWith("https://play.qobuz.com/") || generalQuery.StartsWith("https://open.qobuz.com/"))
        {
            await QobuzApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token);
        }
        else
        {
            var generalSource = await App.GetService<ILocalSettingsService>().ReadSettingAsync<string>(SettingsViewModel.SearchSource) ?? "Deezer";
            switch (generalSource)
            {
                case "Qobuz":
                    await QobuzApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token);
                    break;
                default:
                    await DeezerApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token);
                    break;
            }
        }

        originalList = new ObservableCollection<SongSearchObject>((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource); // Save original list order
        SetResultsAmount(originalList.Count);
        SortCustomListView();
        SetNoSearchResults(); // Call again as actual check 
        SearchProgress.IsIndeterminate = false;
        EnableSearches();
        StopSearchButton.Visibility = Visibility.Collapsed;
    }

    private async Task LoadSpotifyPlaylist(string playlistId, CancellationToken token = default)
    {
        failedSpotifySongs.Clear();
        youtubeAlternateList.Clear();
        FailedListView.ItemsSource = failedSpotifySongs; // Clear the list
        YoutubeWebView.Source = null; // Clear web view source
        ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Clear(); // Clear the list

        List<SongSearchObject> playlistObjects = await SpotifyApi.GetPlaylist(playlistId, token);
        foreach (var song in playlistObjects)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            var deezerResult = await FluentDL.Services.DeezerApi.GetDeezerTrack(song);
            if (deezerResult != null)
            {
                ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Add(deezerResult); // Add to list
            }
            else // Try a fuzzy
            {
                var deezerResult2 = await FluentDL.Services.DeezerApi.GeneralSearch(song);
                if (deezerResult2 != null)
                {
                    ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Add(deezerResult2); // Add to list
                }
                else
                {
                    // Search failed songs on youtube
                    youtubeAlternateList.Add(await YoutubeApi.GetSearchResult(song));
                    failedSpotifySongs.Add(song);
                    FailedListView.ItemsSource = failedSpotifySongs;
                }
            }
        }
    }


    private void SetNoSearchResults()
    {
        // If collection is empty, show a message
        NoSearchResults.Visibility = CustomListView.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FailedDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        failDialogOpen = true;
        var selectedIndex = FailedListView.SelectedIndex;
        if (0 <= selectedIndex && selectedIndex < youtubeAlternateList.Count)
        {
            var youtubeResult = youtubeAlternateList[selectedIndex];
            if (youtubeResult != null)
            {
                var url = youtubeResult.Url;
                // Open the url
                var uri = new Uri(url);
                var success = Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
    }

    // TODO: youtube queue preview is broken
    private void FailedDialog_OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        /*
        var selectedIndex = FailedListView.SelectedIndex;
        if (0 <= selectedIndex && selectedIndex < youtubeAlternateList.Count)
        {
            var youtubeResult = youtubeAlternateList[selectedIndex];
            if (youtubeResult != null)
            {
                var url = youtubeResult.Url;
                // Test thread
                Thread thread = new Thread(async () =>
                {
                    await YoutubeApi.DownloadAudio(url, "download path", youtubeResult.Title);
                    Debug.WriteLine("Finished downloading");
                });
                thread.Start();
            }
        }
        */
        var selectedIndex = FailedListView.SelectedIndex;

        var youtubeObj = (SongSearchObject)FailedListView.SelectedItem;
        if (youtubeObj == null)
        {
            return;
        }

        youtubeObj.Source = "youtube";
        youtubeObj.Id = youtubeAlternateList[selectedIndex].Id;
        QueueViewModel.Add(youtubeObj);
        ShowInfoBar(InfoBarSeverity.Success, $"{youtubeObj.Title} added to queue");
    }

    private void FailedListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedIndex = FailedListView.SelectedIndex;
        if (0 <= selectedIndex && selectedIndex < youtubeAlternateList.Count)
        {
            var youtubeResult = youtubeAlternateList[selectedIndex];
            if (youtubeResult != null)
            {
                var url = "https://www.youtube.com/embed/" + youtubeResult.Id.Value;
                url = youtubeResult.Url;
                YoutubeWebView.Source = new Uri(url); // Set web view source
            }
        }
        else
        {
            YoutubeWebView.Source = null; // Clear web view source
        }
    }

    private void FailedDialog_OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        failDialogOpen = false;
    }

    private void FailDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (failDialogOpen)
        {
            args.Cancel = true;
        }
    }

    private void StopSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        cancellationTokenSource.Cancel();
    }

    private void AddToQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        var listViewCount = (CustomListView.ItemsSource as ObservableCollection<SongSearchObject>).Count;

        if (listViewCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to add");
            return;
        }

        var beforeCount = QueueViewModel.Source.Count;
        foreach (var song in (ObservableCollection<SongSearchObject>)CustomListView.ItemsSource)
        {
            QueueViewModel.Add(song);
        }

        var addedCount = QueueViewModel.Source.Count - beforeCount;

        if (addedCount < listViewCount)
        {
            ShowInfoBar(InfoBarSeverity.Informational, $"Added {addedCount} tracks to queue (duplicates ignored)");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"Added {addedCount} tracks to queue");
        }
    }

    // Functions that open dialogs
    private async void ShowDialog_OnClick_Click(object sender, RoutedEventArgs e)
    {
        SearchDialog.XamlRoot = this.XamlRoot;
        var result = await SearchDialog.ShowAsync();
    }

    private async void FailedResultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FailedDialog.XamlRoot = this.XamlRoot;
        var result = await FailedDialog.ShowAsync();
    }

    // Required for animation to work
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    public void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2)
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

    // Event handler to close the info bar and stop the timer (only ticks once)
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

    private void Help_OnClick(object sender, RoutedEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.ShowMessageDialogAsync("Help message\ntest");
        });
    }
}