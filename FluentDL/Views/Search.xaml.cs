using AngleSharp.Dom;
using AngleSharp.Text;
using FluentDL.Core.Sources;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Windows.Globalization.NumberFormatting;
using Windows.UI.WebUI;
using YoutubeExplode.Search;
using YouTubeMusicAPI.Models.Info;
using static System.Net.WebRequestMethods;
using Visibility = Microsoft.UI.Xaml.Visibility;

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
    private enum SearchMode
    {
        General,
        Advanced
    }

    private ObservableCollection<SongSearchObject> originalList;
    private static bool updateCheckDone;
    private CancellationTokenSource cancellationTokenSource;
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;

    // For incremental searching
    private ScrollViewer? customListViewScrollViewer;
    private bool customListViewBottomLogged;
    private bool isInitial = true;  // Flag to indicate if initial search
    private SearchMode currentSearchMode = SearchMode.General;
    private string generalQuery = "";
    private string advancedArtistName = "";
    private string advancedTrackName = "";
    private string advancedAlbumName = "";
    private int searchOffset = 0;

    // The Search page's source picker lists every searchable source, in catalog order.
    public IReadOnlyList<SourceDescriptor> SearchSources => SourceCatalog.Searchable;

    public static string SearchSourceToolTip(string displayName) => $"Get search results from {displayName}";

    private SourceDescriptor SelectedSearchSource => (SourceDescriptor)SourceRadioButtons.SelectedItem;

    private readonly SourceRegistry sources = App.GetService<SourceRegistry>();
    private readonly DownloadService downloads = App.GetService<DownloadService>();

    // An unknown id searches Deezer, as the old switch statements did.
    private ISearchSource SearchSourceFor(string id) =>
        sources.TryGet<ISearchSource>(id, out var source) ? source : sources.All<ISearchSource>().First(s => s.Descriptor.Id == SourceIds.Deezer);

    private static readonly SemaphoreSlim searchLock = new SemaphoreSlim(1, 1);


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
        QueueViewModel.BulkAddProgressChanged += () => { if (!QueueViewModel.IsClosing) AddToQueueButton.IsEnabled = !QueueViewModel.IsBulkAdding; };
        AddToQueueButton.IsEnabled = !QueueViewModel.IsBulkAdding;
        CustomListView.ItemsSource = new ObservableCollection<SongSearchObject>();
        AttachCollectionChangedEvent((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);
        originalList = new ObservableCollection<SongSearchObject>();
        SetResultsAmount(0);
        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;
        InitPreviewPanelButtons();


        StopSearchButton.Visibility = Visibility.Collapsed;

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
        // Infobar message for possible new version. Check once per launch, even when the check fails. The flag is
        // set before the first await, so another Loaded event can't start a second check.
        var checkForUpdate = !updateCheckDone;
        updateCheckDone = true;
        if (checkForUpdate && await ViewModel.GetNotifyUpdate()) {
            try 
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var update = await App.GetService<FluentDL.Contracts.Services.IAppUpdateService>().CheckAsync(deadline.Token);
                if (update != null)
                {  
                    ShowInfoBar(InfoBarSeverity.Informational, $"{update.DisplayVersion}. Check {SettingsNavigation.Link(SettingsSection.Updates)} to download and install.", 10,
                                buttonText: "Don't Show Again", 
                                onButtonClick: () => 
                                {
                                    _ = ViewModel.SaveNotifyUpdate(false);
                                    ForceHideInfoBar();
                                });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("Could not check for a FluentDL update: {ExceptionType}", ex.GetType().Name);
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
        // SortCustomListView();
    }

    private void CustomListView_Loaded(object sender, RoutedEventArgs e)
    {
        if (customListViewScrollViewer != null)
        {
            return;
        }

        customListViewScrollViewer = FindDescendant<ScrollViewer>(CustomListView);
        if (customListViewScrollViewer != null)
        {
            customListViewScrollViewer.ViewChanged += CustomListViewScrollViewer_ViewChanged;
        }
    }

    private async void CustomListViewScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var atBottom = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 1;
        if (atBottom)
        {
            if (!customListViewBottomLogged)
            {
                customListViewBottomLogged = true;
                try
                {
                    // Debug.WriteLine($"Search ListView bottom reached: {Random.Shared.Next()}");
                    await SearchUtil();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in background search: {ex.ToString()}");
                }
            }
        } else
        { 
            customListViewBottomLogged = false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void InitPreviewPanelButtons()
    {
        // Initialize preview panel command bar
        var addButton = new AppBarButton { Icon = new SymbolIcon(Symbol.Add), Label = "Add to Queue" };
        addButton.Click += async (sender, e) => await AddSongToQueue(PreviewPanel.GetSong());

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

    private async void AddQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        // Retrieve the item from the tag property
        var song = button?.Tag as SongSearchObject;

        await AddSongToQueue(song);
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
        try
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
        } catch (Exception ex)
        {

        }
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
        advancedArtistName = artistNameInput.Text.Trim();
        advancedTrackName = trackNameInput.Text.Trim();
        advancedAlbumName = albumNameInput.Text.Trim();

        if (advancedArtistName.Length == 0 && advancedTrackName.Length == 0 && advancedAlbumName.Length == 0) // If no search query
        {
            return;
        }

        currentSearchMode = SearchMode.Advanced;
        generalQuery = "";
        searchOffset = 0;
        isInitial = true;

        var itemSource = (ObservableCollection<SongSearchObject>)CustomListView.ItemsSource;
        itemSource.Clear();
        originalList.Clear();

        SearchProgress.IsIndeterminate = true;
        NoSearchResults.Visibility = Visibility.Collapsed; // Hide the message for now
        StopSearchButton.Visibility = Visibility.Visible; // Make stop button visible
        SearchBox.Text = ""; // Clear regular search box query
        DisableSearches();
        cancellationTokenSource = new CancellationTokenSource(); // Reset the cancel token

        await SearchUtil();
    }

    private async Task SearchUtil()
    {
        if (searchLock.Wait(0))
        {
            try
            {
                SearchProgress.IsIndeterminate = true;
                StopSearchButton.Visibility = Visibility.Visible;
                NoSearchResults.Visibility = Visibility.Collapsed; // Hide the message for now
                DisableSearches();
                cancellationTokenSource = new CancellationTokenSource(); // Reset the cancel token

                // Check if query is a spotify playlist link
                // Format of links https://open.spotify.com/playlist/{id}?
                // OR https://open.spotify.com/playlist/{id}?...

                UrlStatusUpdateCallback statusUpdate = (level, message, duration) =>
                {
                    if (duration == -1)
                    {
                        ShowInfoBarPermanent(level.ToSeverity(), message);
                        InfobarProgress.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        InfobarProgress.Visibility = Visibility.Collapsed;
                        ShowInfoBar(level.ToSeverity(), message, duration);
                    }
                };

                var searchSource = SelectedSearchSource;
                var generalSource = searchSource.Id;
                var itemSource = (ObservableCollection<SongSearchObject>)CustomListView.ItemsSource;
                int oldSize = itemSource.Count;
                var searchLimit = isInitial ? searchSource.InitialChunkSize : searchSource.SearchChunkSize;

                try
                {
                    if (currentSearchMode == SearchMode.Advanced)
                    {
                        var query = new AdvancedQuery(advancedArtistName, advancedTrackName, advancedAlbumName);
                        await SearchSourceFor(generalSource).AdvancedSearchAsync(itemSource, query, new PageRequest(searchOffset, searchLimit), cancellationTokenSource.Token);
                        searchOffset += searchLimit;
                    }
                    else if (sources.FindLinkHandler(generalQuery) is { } linkSource)
                    {
                        // Most links load everything on the first page; Qobuz label and artist links load page by page.
                        var request = new LinkRequest(isInitial, new PageRequest(searchOffset, searchLimit), ViewModel.AlbumMode);
                        await linkSource.AddFromLinkAsync(itemSource, generalQuery, request, statusUpdate, cancellationTokenSource.Token);
                        searchOffset += searchLimit;
                    }
                    else
                    {
                        await SearchSourceFor(generalSource).SearchAsync(itemSource, generalQuery, new PageRequest(searchOffset, searchLimit), ViewModel.AlbumMode, cancellationTokenSource.Token);
                        searchOffset += searchLimit;
                    }
                }
                catch (SourceSignInRequiredException error)
                {
                    ShowSignInRequired(error);
                }
                catch (OperationCanceledException)
                {
                    // Stopped by the user; keep what loaded.
                }
                catch (Exception e)
                {
                    // This runs in an async void event handler, so an exception from a source used to crash the app (bug 20).
                    Serilog.Log.Error(e, "Search or link import failed; source {Source}", generalSource);
                    InfobarProgress.Visibility = Visibility.Collapsed;
                    ShowInfoBar(InfoBarSeverity.Error, $"Search failed: {e.Message}", 5);
                }

                // Add new items to the originalList
                foreach (var newItem in itemSource.Skip(oldSize))
                {
                    originalList.Add(newItem);
                }
            }
            finally
            {
                isInitial = false; // Reset initial search flag
                SetResultsAmount(originalList.Count);
                //SortCustomListView();
                SetNoSearchResults(); // Call again as actual check 
                SearchProgress.IsIndeterminate = false;
                EnableSearches();
                StopSearchButton.Visibility = Visibility.Collapsed;

                searchLock.Release();
            }
        }
    }

    private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var queryText = SearchBox.Text.Trim();
        if (queryText.Length == 0)
        {
            return;
        }

        // Reset sort options
        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;

        currentSearchMode = SearchMode.General;
        this.generalQuery = queryText; // Save the query for incremental searching
        searchOffset = 0; // Reset offset for new search

        // New search, clear the list first
        var itemSource = (ObservableCollection<SongSearchObject>)CustomListView.ItemsSource;
        itemSource.Clear();
        originalList.Clear();

        isInitial = true; // Set initial search flag
        await SearchUtil();
    }

    private void SetNoSearchResults()
    {
        // If collection is empty, show a message
        NoSearchResults.Visibility = CustomListView.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        cancellationTokenSource.Cancel();
    }

    private async void AddToQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueViewModel.IsBulkAdding) return;
        var snapshot = ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).ToArray();
        var listViewCount = snapshot.Length;

        if (listViewCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No tracks to add");
            return;
        }

        var large = listViewCount >= QueueViewModel.BulkAddThreshold;
        if (large) AddToQueueButton.IsEnabled = false;
        try
        {
            var addedCount = await QueueViewModel.AddManyAsync(snapshot, text =>
            {
                dispatcherTimer.Stop();
                InfobarProgress.Visibility = Visibility.Visible;
                ShowInfoBarPermanent(InfoBarSeverity.Informational, text,
                    isIndeterminate: false, progressPercent: QueueViewModel.BulkAddProgress / 100);
            });
            if (QueueViewModel.IsClosing) return;

            if (large)
            {
                ShowInfoBar(QueueViewModel.BulkAddSeverity, QueueViewModel.BulkAddText, 5);
            }
            else if (addedCount < listViewCount)
            {
                ShowInfoBar(InfoBarSeverity.Informational, $"Added {addedCount} {(addedCount == 1 ? "item" : "items")} to queue (duplicates ignored)");
            }
            else
            {
                ShowInfoBar(InfoBarSeverity.Success, $"Added {addedCount} {(addedCount == 1 ? "item" : "items")} to queue");
            }
        }
        catch (Exception ex)
        {
            if (!QueueViewModel.IsClosing) ShowInfoBar(InfoBarSeverity.Error, $"Error adding to queue: {ex.Message}");
        }
        finally
        {
            if (!QueueViewModel.IsClosing)
            {
                AddToQueueButton.IsEnabled = !QueueViewModel.IsBulkAdding;
                InfobarProgress.Visibility = Visibility.Collapsed;
                InfobarProgress.IsIndeterminate = true;
            }
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

    // Required for animation to work
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object sender, object e)
    {
        if (QueueViewModel.IsClosing) { dispatcherTimer.Stop(); return; }
        PageInfoBar.Opacity = 0;
        (sender as DispatcherTimer).Stop();
        // Set IsOpen to false after 0.25 seconds
        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            dispatcher.TryEnqueue(() =>
            {
                if (QueueViewModel.IsClosing) return;
                try
                {
                    PageInfoBar.IsOpen = false;
                } catch
                {

                }
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
    private void UpdateSourceButtonUI(string source, bool isAlbumMode)
    {
        var brush = SourceBrushes.For(source);

        // Apply color to both shapes
        SourceButtonEllipse.Fill = brush;
        SourceButtonFontIcon.Foreground = brush;

        // Toggle visibility based on Album Mode
        if (isAlbumMode)
        {
            SourceButtonFontIcon.Visibility = Visibility.Visible;
            SourceButtonEllipse.Visibility = Visibility.Collapsed;
        }
        else
        {
            SourceButtonFontIcon.Visibility = Visibility.Collapsed;
            SourceButtonEllipse.Visibility = Visibility.Visible;
        }
    }

    private async void FlyoutBase_OnClosed(object? sender, object e)
    {
        var selectedSource = SelectedSearchSource.Id;

        // Save settings
        await ViewModel.SaveSearchSource(selectedSource);
        await ViewModel.SaveResultsLimit();
        await ViewModel.SaveAlbumMode();

        bool isAlbumModeActive = ViewModel.AlbumMode;

        // Update the UI
        UpdateSourceButtonUI(selectedSource, isAlbumModeActive);
    }

    private async Task InitializeSource()
    {
        var source = await ViewModel.GetSearchSource();
        source = source.ToLower();

        // -1 (nothing selected) for an unknown saved source, as before
        var index = SearchSources.Select(s => s.Id).ToList().IndexOf(source);
        SourceRadioButtons.SelectedIndex = index;

        if (SourceRadioButtons.SelectedIndex != -1)
        {
            SourceRadioButtons.SelectedItem = SearchSources[SourceRadioButtons.SelectedIndex];
        }

        bool isAlbumModeActive = ViewModel.AlbumMode;

        UpdateSourceButtonUI(source, isAlbumModeActive);
    }


    // HELPER FUNCTIONS ----------------------------------------------------------------------------------------------------
    private async Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) =>
        sources.TryGet<ICatalogSource>(album.Source, out var catalog) ? await catalog.GetAlbumTracksAsync(album) : [];

    private async Task AddSongToQueue(SongSearchObject? song)
    {
        if (song == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "No song selected"); // Technically shouldn't happen
            return;
        }

        var beforeCount = QueueViewModel.Source.Count;
        QueueViewModel.Add(song); // an album is one row
        var label = song is AlbumSearchObject ? "Album" : "Track";

        if (QueueViewModel.Source.Count == beforeCount) // No change
        {
            ShowInfoBar(InfoBarSeverity.Warning, $"{label} <a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> already in queue");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Success, $"{label} <a href='{ApiHelper.GetUrl(song)}'>{song.Title}</a> added to queue");
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
        if (ApplicationActivity.Gate.IsInstalling) { ShowInfoBar(InfoBarSeverity.Warning, "Wait for the app update to finish."); return; }
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (songObj == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to download track");
            return;
        }

        if (songObj.Source == SourceIds.Local)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Track is already local");
            return;
        }

        try { await downloads.EnsureDownloadAccessAsync([songObj]); }
        catch (SourceSignInRequiredException error) { ShowSignInRequired(error); return; }

        // Create a folder picker (for download directory)
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);

        // If user needs to select a directory
        if (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory))
        {
            directory = await StoragePickerHelper.GetDirectory();
            if (directory == null)
            {
                ShowInfoBar(InfoBarSeverity.Informational, "Download cancelled. No folder selected.");
                return;
            }
        }

        ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Saving <a href='{ApiHelper.GetUrl(songObj)}'>{songObj.Title}</a> to <a href='{directory}'>{directory}</a>", title: "Download in Progress");

        InfobarProgress.Visibility = Visibility.Visible; // Show the infobar's progress bar

        // Download notification helper
        var downloadHelper = new DownloadProgressHelper($"<a href='{ApiHelper.GetUrl(songObj)}'>{directory}</a>");
        downloadHelper.ProgressUpdated += (s, e) => {
            var finalStr = e.DisplayString;
            var progress = e.ProgressValue;
            var preparing = e.Preparing;
            dispatcher.TryEnqueue(() =>
            {
                if (!downloadHelper.IsRunning) return;
                ShowInfoBarPermanent(InfoBarSeverity.Informational, finalStr, title: "Downloading", isIndeterminate: preparing, progressPercent: progress);
            });
        };

        if (songObj is AlbumSearchObject albumObj)
        {
            downloadHelper.SetTracksCount(albumObj.TracksCount);
            downloadHelper.StartLoop();
            var progress = new Progress<ProgressData>(downloadHelper.UpdateProgressLoop);
            List<string>? paths = [];
            try
            {
                paths = await downloads.DownloadAlbum(albumObj, directory, progress);
            }
            catch (SourceSignInRequiredException error)
            {
                downloadHelper.StopLoop();
                ShowSignInRequired(error);
                return;
            }
            catch (Exception ex)
            {
                ShowInfoBar(InfoBarSeverity.Error, ex.Message, seconds: 10);
                paths = null;  // Set to null (error happened)
            }

            downloadHelper.StopLoop();

            if (paths != null)  // Not error
            {
                var failedTracks = 0;
                foreach (var path in paths)
                {
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    {
                        try
                        {
                            await LocalExplorerViewModel.AddSongFromFile(path);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to add downloaded song to local explorer: {ex.Message}");
                            failedTracks++;
                        }
                    }
                }

                Action navigateLocalExplorer = () =>
                {
                    var navService = App.GetService<INavigationViewService>();
                    navService.NavigateTo(typeof(LocalExplorerViewModel).FullName);
                };

                var albumDir = paths.Count > 0 ? System.IO.Path.GetDirectoryName(paths[0]) : "";

                if (paths.Count == 0)
                {
                    ShowInfoBar(InfoBarSeverity.Error, $"Failed to download album '{albumObj.AlbumName}'", seconds: 10);
                }
                else if (paths.Count - failedTracks < albumObj.TracksCount)
                {
                    ShowInfoBar(InfoBarSeverity.Warning, $"Downloaded {paths.Count - failedTracks} of {albumObj.TracksCount} tracks for album <a href='{albumDir}'>{albumObj.Title}</a>", 10, buttonText: "View Download", onButtonClick: navigateLocalExplorer);
                }
                else
                {
                    ShowInfoBar(InfoBarSeverity.Success, $"Successfully downloaded album <a href='{albumDir}'>{albumObj.Title}</a>.", 10, buttonText: "View Download", onButtonClick: navigateLocalExplorer);
                }
            }

            InfobarProgress.Visibility = Visibility.Collapsed; // Hide the infobar's progress bar
            InfobarProgress.IsIndeterminate = true;
        }
        else
        {
            var progress = new Progress<ProgressData>(downloadHelper.UpdateProgressLoop);
            downloadHelper.StartLoop();

            string path;
            try
            {
                path = await downloads.DownloadObject(songObj, directory, progress, (severity, song, location) =>
            {
                dispatcher.TryEnqueue(() =>
                {
                    downloadHelper.StopLoop();
                    InfobarProgress.Visibility = Visibility.Collapsed; // Hide the infobar's progress bar

                    Action navigateLocalExplorer = () =>
                    {
                        var navService = App.GetService<INavigationViewService>();
                        navService.NavigateTo(typeof(LocalExplorerViewModel).FullName);
                    };

                    if (severity == StatusLevel.Error)
                    {
                        ShowInfoBar(severity.ToSeverity(), $"Error: {location ?? "unspecified"}", 5);
                    }
                    else if (severity == StatusLevel.Success)
                    {
                        ShowInfoBar(severity.ToSeverity(), $"Successfully downloaded <a href='{location}'>{songObj.Title}</a>", 10, buttonText: "View Download", onButtonClick: navigateLocalExplorer);
                    }
                    else if (severity == StatusLevel.Warning)
                    {
                        ShowInfoBar(severity.ToSeverity(), $"Downloaded a possible equivalent of <a href='{location}'>{songObj.Title}</a>", 10, buttonText: "View Download", onButtonClick: navigateLocalExplorer);
                    }
                });
                });
            }
            catch (SourceSignInRequiredException error)
            {
                downloadHelper.StopLoop();
                ShowSignInRequired(error);
                return;
            }
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                try
                {
                    await LocalExplorerViewModel.AddSongFromFile(path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to add downloaded song to local explorer: {ex.Message}");
                }
            }
            // The result callback normally stops it; this covers paths that return without one.
            if (downloadHelper.IsRunning)
            {
                downloadHelper.StopLoop();
                InfobarProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private RoutedEventHandler? infoBarClick;
    private void ShowSignInRequired(SourceSignInRequiredException error)
    {
        if (SettingsNavigation.ForSource(error.SourceId) is not { } section) return;
        ShowSettingsNotice(error.Message, section);
    }

    private void ShowSettingsNotice(string message, SettingsSection section)
    {
        ShowInfoBarPermanent(InfoBarSeverity.Warning, SettingsNavigation.WithLink(message, section), isIndeterminate: false);
        InfobarProgress.Visibility = Visibility.Collapsed;
    }

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2, string title = "", string buttonText = "", Action? onButtonClick = null)
    {
        title = title.Trim();
        message = message.Trim();
        dispatcher.TryEnqueue(() =>
        {
            dispatcherTimer.Stop();
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            InfoBarWidth.Update(InfobarContent, hold: false);
            //PageInfoBar.Title = title;

            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>   {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }

            if (infoBarClick != null) InfobarButton.Click -= infoBarClick;
            infoBarClick = null;
            if (onButtonClick != null)
            {
                InfobarButton.Visibility = Visibility.Visible;
                InfobarButton.Content = buttonText;
                infoBarClick = (_, _) => onButtonClick();
                InfobarButton.Click += infoBarClick;
            }
            else
            {
                InfobarButton.Visibility = Visibility.Collapsed;
            }
            dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
            dispatcherTimer.Start();
        });
    }

    private void ShowInfoBarPermanent(InfoBarSeverity severity, string message, string title = "", string buttonText = "", Action? onButtonClick = null, bool isIndeterminate = true, double progressPercent = 0)
    {
        title = title.Trim();
        message = message.Trim();
        dispatcher.TryEnqueue(() =>
        {
            if (QueueViewModel.IsClosing) return;
            dispatcherTimer.Stop();
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            InfobarProgress.IsIndeterminate = isIndeterminate;
            InfobarProgress.Value = Math.Clamp(progressPercent * 100, 0, 100);
            InfoBarWidth.Update(InfobarContent, hold: !isIndeterminate); // a percentage is showing
            //PageInfoBar.Title = title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>    {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }

            if (infoBarClick != null) InfobarButton.Click -= infoBarClick;
            infoBarClick = null;
            if (onButtonClick != null)
            {
                InfobarButton.Visibility = Visibility.Visible;
                InfobarButton.Content = buttonText;
                infoBarClick = (_, _) => onButtonClick();
                InfobarButton.Click += infoBarClick;
            }
            else
            {
                InfobarButton.Visibility = Visibility.Collapsed;
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
