using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;
using FluentDL.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using Microsoft.UI.Dispatching;
using YoutubeExplode.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Visibility = Microsoft.UI.Xaml.Visibility;
using static System.Net.WebRequestMethods;
using System.Drawing;
using Microsoft.UI.Xaml.Media;

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
    public delegate void UrlStatusUpdateCallback(InfoBarSeverity severity, string message, int duration = 2); // Callback for url status update (for infobars)

    private TerminalSubprocess _terminalSubprocess;
    private ObservableCollection<SongSearchObject> originalList;
    private SpotifyApi spotifyApi;
    private ObservableCollection<SongSearchObject> failedSpotifySongs;
    private List<VideoSearchResult> youtubeAlternateList;
    private bool failDialogOpen = false, updateNotificationGiven = false;
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

        this.Loaded += SearchPage_Loaded;
        InitAnimation();
    }

    private void InitAnimation()
    {
        // Animation for add to queue button
        AnimationHelper.AttachScaleAnimation(AddToQueueButton, ResultsIcon);

        // Animation for advanced search button
        AnimationHelper.AttachScaleAnimation(ShowDialogButton, ShowDialogIcon);

        // Animation for help button
        //AnimationHelper.AttachScaleAnimation(HelpButton, HelpIcon);
    }

    private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync(); // Initialize settings
        await InitializeSource();  // Update source button color

        //ResultsIcon.Loaded += (s, e) => InitAnimation();
        // Infobar message for possible new version
        if (!updateNotificationGiven && await ViewModel.GetNotifyUpdate()) {
            try 
            {
                var latestRelease = await GithubAPI.GetLatestRelease() ?? "";
                var currentVersion = SettingsViewModel.GetVersionDescription();
                if (latestRelease.CompareTo(currentVersion) > 0)  // Latest version is lexicographically greater
                {  
                    ShowInfoBar(InfoBarSeverity.Informational, $"New version available: <a href='https://github.com/derekyang2/fluentdl/releases/latest'>FluentDL {latestRelease}</a>", 5);
                }
                updateNotificationGiven = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to fetch latest release version: " + ex);
            }
        }
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e) // Navigated to page
    {
        await ViewModel.InitializeAsync(); // Initialize settings
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong != null)
        {
            PreviewPanel.Show();
            await PreviewPanel.Update(selectedSong);
        }

        // Refresh all list view items
        SortCustomListView();
    }

    private void InitPreviewPanelButtons()
    {
        // Initialize preview panel command bar
        var addButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add to Queue" };
        addButton.Click += (sender, e) => AddSongToQueue(PreviewPanel.GetSong());

        var downloadButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Download), Label = "Download" };
        downloadButton.Click += async (sender, e) => await DownloadSong(PreviewPanel.GetSong());

        var openButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8A7" }, Label = "Open" };
        openButton.Click += (sender, e) => OpenSongInBrowser(PreviewPanel.GetSong());
        
        var shareButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Link), Label = "Link" };
        shareButton.Click += (sender, e) => CopySongLink(PreviewPanel.GetSong());

        // Init animations
        AnimationHelper.AttachScaleAnimation(addButton);
        AnimationHelper.AttachSpringDownAnimation(downloadButton);
        AnimationHelper.AttachScaleAnimation(shareButton);
        AnimationHelper.AttachSpringUpAnimation(openButton);

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { addButton, downloadButton, shareButton, openButton });
    }

    private void AddQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;

        AddSongToQueue(song);
    }
    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;

        await DownloadSong(song);
    }

    private void ShareLinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;

        CopySongLink(song);
    }

    private void OpenInBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;

        OpenSongInBrowser(song);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) // Navigated away from page
    {
        PreviewPanel.Clear(); // Clear preview 
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
        var generalSource = (SourceRadioButtons.SelectedItem as RadioButton).Content.ToString().ToLower(); 

        switch (generalSource)
        {
            case "spotify":
                await FluentDL.Services.SpotifyApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                break;
            case "qobuz":
                await FluentDL.Services.QobuzApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                break;
            case "youtube":
                await FluentDL.Services.YoutubeApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                break;
            default:
                await FluentDL.Services.DeezerApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                break;
        }

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

        UrlStatusUpdateCallback statusUpdate = (severity, message, duration) =>
        {
            if (duration == -1)
            {
                ShowInfoBarPermanent(severity, message);
                InfobarProgress.Visibility = Visibility.Visible;
            }
            else
            {
                InfobarProgress.Visibility = Visibility.Collapsed;
                ShowInfoBar(severity, message, duration);
            }
        };


        if (generalQuery.StartsWith("https://open.spotify.com/"))
        {
            await SpotifyApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, statusUpdate);

            /*
            var playlistId = generalQuery.Split("/").Last();
            //Spotify to deezer conversion
            await LoadSpotifyPlaylist(playlistId, cancellationTokenSource.Token);
            */
        }
        else if (generalQuery.StartsWith("https://deezer.page.link") || System.Text.RegularExpressions.Regex.IsMatch(generalQuery, @"https://www\.deezer\.com(/[^/]+)?/(track|album|playlist)/.*"))
        {
            await DeezerApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, statusUpdate);
        }
        else if (generalQuery.StartsWith("https://www.qobuz.com/") || generalQuery.StartsWith("https://play.qobuz.com/") || generalQuery.StartsWith("https://open.qobuz.com/"))
        {
            await QobuzApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, statusUpdate);
        }
        else if (generalQuery.StartsWith("https://www.youtube.com/") || generalQuery.StartsWith("https://youtube.com/") || generalQuery.StartsWith("https://music.youtube.com/"))
        {
            await YoutubeApi.AddTracksFromLink((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, statusUpdate);
        }
        else
        {
            var generalSource = (SourceRadioButtons.SelectedItem as RadioButton).Content.ToString().ToLower();

            switch (generalSource)
            {
                case "qobuz":
                    await QobuzApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                    break;
                case "spotify":
                    await SpotifyApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                    break;
                case "youtube":
                    await YoutubeApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, ViewModel.ResultsLimit);
                    break;
                default:
                    await DeezerApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, cancellationTokenSource.Token, ViewModel.ResultsLimit);
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

    /*
     // Old conversion method
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
    */

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
        try {
            var result = await SearchDialog.ShowAsync();
        } catch (Exception err) {
            Debug.WriteLine(err.Message);
        }
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
            App.MainWindow.ShowMessageDialogAsync("\u2022 The only source that requires authentication for searches is Spotify (downloading differs).\n\n\u2022 The search page allows you to look up tracks from various sources.\n\n\u2022 You may use the drop down on the search bar to change the source or do so in settings.\n\n\u2022 The search bar performs a general search while the advanced search button performs a filtered search.\n\n\u2022 You may also directly paste links for tracks, albums, and playlists into the search bar (only Qobuz playlist links unsupported).\n\n\u2022 Add songs to queue to perform more tasks on them.", "Help");
        });
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

    // Save search results limit
    private async void FlyoutBase_OnClosed(object? sender, object e)
    {
        // Get selected item in radio buttons
        var selectedSource = (SourceRadioButtons.SelectedItem as RadioButton).Content.ToString().ToLower(); 

        // Update source button color
        SourceButtonEllipse.Fill = (selectedSource) switch
        {
            "spotify" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 213, 101)),
            "deezer" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 55, 250)),
            "qobuz" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
            "youtube" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), // Local source or anything else
        };

        // Save settings
        await ViewModel.SaveSearchSource(selectedSource);
        await ViewModel.SaveResultsLimit();

    }

    private async Task InitializeSource() {
        var source = await ViewModel.GetSearchSource();
        source = source.ToLower();

        SourceRadioButtons.SelectedIndex = (source) switch
        {
            "deezer" => 0,
            "qobuz" => 1,
            "spotify" => 2,
            "youtube" => 3,
            _ => -1,
        };
        SourceRadioButtons.SelectedItem = SourceRadioButtons.Items.ElementAt(SourceRadioButtons.SelectedIndex);

        // Update source button color
        SourceButtonEllipse.Fill = (source) switch
        {
            "spotify" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 213, 101)),
            "deezer" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 55, 250)),
            "qobuz" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
            "youtube" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), // Local source or anything else
        };
    }


    // HELPER FUNCTIONS ----------------------------------------------------------------------------------------------------
    private void AddSongToQueue(SongSearchObject? song)
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

    private static void OpenSongInBrowser(SongSearchObject? song)
    {
        if (song != null)
        {
            var uri = new Uri(ApiHelper.GetUrl(song));
            Windows.System.Launcher.LaunchUriAsync(uri); // Open link in browser
        }
    }
    async Task DownloadSong(SongSearchObject? songObj)
    {
        if (songObj == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to download track");
            return;
        }

        if (songObj.Source == "local")
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Track is already local");
            return;
        }

        // Create a folder picker (for download directory)
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);

        // If user needs to select a directory
        if (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory))
        {
            directory = await StoragePickerHelper.GetDirectory();
            if (directory == null)
            {
                ShowInfoBar(InfoBarSeverity.Warning, "No download directory selected", 3);
                return;
            }
        }

        ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Saving <a href='{ApiHelper.GetUrl(songObj)}'>{songObj.Title}</a> to <a href='{directory}'>{directory}</a>", title: "Download in Progress");

        InfobarProgress.Visibility = Visibility.Visible; // Show the infobar's progress bar

        await ApiHelper.DownloadObject(songObj, directory, (severity, song, location) => {
            dispatcher.TryEnqueue(()=>{
                                InfobarProgress.Visibility = Visibility.Collapsed; // Hide the infobar's progress bar
                if (severity == InfoBarSeverity.Error)
                {
                    ShowInfoBar(severity, $"Error: {location ?? "unknown"}", 5);
                }
                else if (severity == InfoBarSeverity.Success)
                {
                    ShowInfoBar(severity, $"Successfully downloaded <a href='{location}'>{songObj.Title}</a>", 5);
                }
                else if (severity == InfoBarSeverity.Warning)
                {
                    ShowInfoBar(severity, $"Downloaded a possible equivalent of <a href='{location}'>{songObj.Title}</a>", 5);
                }
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

    private void ForceHideInfoBar()
    {
        dispatcher.TryEnqueue(() =>
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
                    dispatcher.TryEnqueue(() =>
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
}