using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using FluentDL.Services;
using System.Collections.ObjectModel;
using Windows.Media.Playback;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

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
    private RipSubprocess ripSubprocess;
    private ObservableCollection<SongSearchObject> originalList;
    private SpotifyApi spotifyApi;

    public BlankViewModel ViewModel
    {
        get;
    }

    public Search()
    {
        ViewModel = App.GetService<BlankViewModel>();
        InitializeComponent();
        CustomListView.ItemsSource = new ObservableCollection<SongSearchObject>();
        originalList = new ObservableCollection<SongSearchObject>();
        ResultsText.Text = "0 results";
        ripSubprocess = new RipSubprocess();
        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;
        ClearPreviewPane();
        SpotifyApi.Initialize();
    }

    public void SetResultsAmount(int amount)
    {
        ResultsText.Text = amount + (amount == 1 ? " result" : " results");
    }

    private void ClearPreviewPane()
    {
        NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        SongPreviewPlayer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        CommandBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PreviewScrollView.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        PreviewTitleText.Text = "";
        PreviewImage.Source = null;
        PreviewInfoControl.ItemsSource = new List<TrackDetail>();
        PreviewInfoControl2.ItemsSource = new List<TrackDetail>();
    }

    private async void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            ClearPreviewPane();
            return;
        }

        NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        SongPreviewPlayer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        CommandBar.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        PreviewScrollView.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        await SetupPreviewPane(selectedSong);

        // PreviewTitleText.Text = selectedSong.Title + " " + selectedSong.Artists + " " + selectedSong.Id;

        // Send a request to /track/{id} for more information on this song
        /*
        var uiThread = DispatcherQueue.GetForCurrentThread();
        new Task(async () =>
        {
            var jsonObject = await FluentDL.Services.DeezerApi.FetchJsonElement("track/" + selectedSong.Id);

            uiThread.TryEnqueue(() =>
            {
                PreviewArtistText.Text = jsonObject.GetProperty("artist").GetProperty("name").ToString();
                PreviewTitleText.Text = jsonObject.GetProperty("title").ToString();
                PreviewLinkText.Text = jsonObject.GetProperty("link").ToString();
            });

            var mediaPlayer = new MediaPlayer();
            mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(jsonObject.GetProperty("preview").ToString()));
            mediaPlayer.Play();
        }).Start();
        */
    }

    private async Task SetupPreviewPane(SongSearchObject selectedSong)
    {
        var jsonObject = await FluentDL.Services.DeezerApi.FetchJsonElement("track/" + selectedSong.Id);
        //PreviewArtistText.Text = selectedSong.Artists;
        //PreviewReleaseDate.Text = selectedSong.ReleaseDate; // Todo format date
        //PreviewRank.Text = selectedSong.Rank; // Todo format rank
        //PreviewDuration.Text = selectedSong.Duration;
        // PreviewAlbumName.Text = jsonObject.GetProperty("album").GetProperty("title").GetString();
        // PreviewAlbumPosition.Text = jsonObject.GetProperty("track_position").ToString();
        PreviewTitleText.Text = selectedSong.Title;

        PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = new List<TrackDetail>
        {
            new TrackDetail { Label = "Artists", Value = selectedSong.Artists },
            new TrackDetail
            {
                Label = "Release Date",
                Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString()
            },
            new TrackDetail { Label = "Popularity", Value = selectedSong.Rank },
            new TrackDetail
            {
                Label = "Duration",
                Value = new DurationConverter().Convert(selectedSong.Duration, null, null, null).ToString()
            },
            new TrackDetail { Label = "Album", Value = selectedSong.AlbumName },
            new TrackDetail { Label = "Track", Value = jsonObject.GetProperty("track_position").ToString() }
        };

        // Set 30 second preview
        SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(jsonObject.GetProperty("preview").ToString()));

        PreviewImage.Source = new BitmapImage(new Uri(jsonObject.GetProperty("album").GetProperty("cover_big").ToString()));
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
    }

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
        NoSearchResults.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; // Hide the message for now

        // Set the collection as the ItemsSource for the ListView
        await FluentDL.Services.DeezerApi.AdvancedSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, artistName, trackName, albumName, ResultsText);
        originalList = new ObservableCollection<SongSearchObject>((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);

        SetResultsAmount(originalList.Count);
        SortCustomListView();
        SetNoSearchResults(); // Call again as actual check 
        SearchProgress.IsIndeterminate = false;
    }

    private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var generalQuery = SearchBox.Text.Trim();
        if (generalQuery.Length == 0)
        {
            return;
        }

        SearchProgress.IsIndeterminate = true;
        // Set the collection as the ItemsSource for the ListView
        NoSearchResults.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; // Hide the message for now

        // Check if query is a spotify playlist link
        // Format of links https://open.spotify.com/playlist/{id}?
        // OR https://open.spotify.com/playlist/{id}?...
        if (generalQuery.Contains("https://open.spotify.com/playlist/"))
        {
            var playlistId = generalQuery.Split("/").Last();
            // Remove any query parameters
            if (playlistId.Contains("?"))
            {
                playlistId = playlistId.Split("?").First();
            }

            await LoadSpotifyPlaylist(playlistId);
        }
        else
        {
            await FluentDL.Services.DeezerApi.GeneralSearch((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource, generalQuery, ResultsText);
        }

        originalList = new ObservableCollection<SongSearchObject>((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource);

        SetResultsAmount(originalList.Count);
        SortCustomListView();
        SetNoSearchResults(); // Call again as actual check 
        SearchProgress.IsIndeterminate = false;
    }

    private async Task LoadSpotifyPlaylist(string playlistId)
    {
        ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Clear(); // Clear the list
        var resultsCt = 0;

        List<SongSearchObject> playlistObjects = await SpotifyApi.GetPlaylist(playlistId);
        foreach (var song in playlistObjects)
        {
            var firstArtist = song.Artists.Split(",")[0];
            var deezerResult = await FluentDL.Services.DeezerApi.AdvancedSearch(firstArtist, song.Title, song.AlbumName);
            if (deezerResult != null)
            {
                ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Add(deezerResult); // Add to list
                SetResultsAmount(++resultsCt);
            }
            else // Try a fuzzy
            {
                var deezerResult2 = await FluentDL.Services.DeezerApi.GeneralSearch(song);
                if (deezerResult2 != null)
                {
                    ((ObservableCollection<SongSearchObject>)CustomListView.ItemsSource).Add(deezerResult2); // Add to list
                    SetResultsAmount(++resultsCt);
                }
                else
                {
                    Debug.WriteLine("Could not find: " + song);
                }
            }
        }
    }


    private async void ShowDialog_OnClick_Click(object sender, RoutedEventArgs e)
    {
        SearchDialog.XamlRoot = this.XamlRoot;
        var result = await SearchDialog.ShowAsync();
    }

    private void SetNoSearchResults()
    {
        // If collection is empty, show a message
        NoSearchResults.Visibility = CustomListView.Items.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}