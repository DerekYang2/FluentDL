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

public sealed partial class Search : Page
{
    private RipSubprocess ripSubprocess;
    private List<SongSearchObject> originalList;

    public BlankViewModel ViewModel
    {
        get;
    }

    public Search()
    {
        ViewModel = App.GetService<BlankViewModel>();
        InitializeComponent();
        ripSubprocess = new RipSubprocess();
        SortComboBox.SelectedIndex = 0;
        SortOrderComboBox.SelectedIndex = 0;
        SongPreviewPlayer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void SearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var artistName = artistNameInput.Text;
        var trackName = trackNameInput.Text;

        SearchProgress.IsIndeterminate = true;
        // Set the collection as the ItemsSource for the ListView
        CustomListView.ItemsSource = await FluentDL.Services.DeezerApi.SearchTrack(artistName, trackName);
        originalList = new List<SongSearchObject>((List<SongSearchObject>)CustomListView.ItemsSource);

        SortCustomListView();

        // If collection is empty, show a message
        NoSearchResults.Visibility = CustomListView.Items.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        SearchProgress.IsIndeterminate = false;

        //Debug.WriteLine(ripSubprocess.RunCommandSync("rip config path"));

        //testTextBlock.Text = debugText;

        /*
        testTextBlock.Text = debugText;
        // Run a "rip" (python library) command in the terminal
        Process cmd = new Process();
        cmd.StartInfo.FileName = "cmd.exe";
        cmd.StartInfo.RedirectStandardInput = true;
        cmd.StartInfo.RedirectStandardOutput = true;
        cmd.StartInfo.CreateNoWindow = true;
        cmd.StartInfo.UseShellExecute = false;
        cmd.Start();

        cmd.StandardInput.WriteLine("rip config path");
        cmd.StandardInput.Flush();
        cmd.StandardInput.Close();
        cmd.WaitForExit();
        Debug.WriteLine("OUTPUT: " + cmd.StandardOutput.ReadToEnd());
        */
    }

    private async void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PreviewArtistText.Text = "";
            PreviewTitleText.Text = "";
            PreviewLinkText.Text = "";
            SongPreviewPlayer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        SongPreviewPlayer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        await SetupPreviewPane(selectedSong.Id);

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

    private async Task SetupPreviewPane(string trackId)
    {
        var jsonObject = await FluentDL.Services.DeezerApi.FetchJsonElement("track/" + trackId);
        PreviewArtistText.Text = jsonObject.GetProperty("artist").GetProperty("name").ToString();
        PreviewTitleText.Text = jsonObject.GetProperty("title").ToString();
        PreviewLinkText.Text = jsonObject.GetProperty("link").ToString();
        SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(jsonObject.GetProperty("preview").ToString()));
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

        var songList = (List<SongSearchObject>)CustomListView.ItemsSource;
        if (songList == null)
        {
            return;
        }

        switch (selectedIndex)
        {
            case 0:
                songList = new List<SongSearchObject>(originalList); // <SongSearchObject
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

        CustomListView.ItemsSource = songList;
    }
}