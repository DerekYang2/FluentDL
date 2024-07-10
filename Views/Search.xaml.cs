using System.Diagnostics;
using System.Text.Json;
using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using FluentDL.Services;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

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
    }

    private async void SearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var artistName = artistNameInput.Text;
        var trackName = trackNameInput.Text;

        SearchProgress.IsIndeterminate = true;
        // Set the collection as the ItemsSource for the ListView
        CustomListView.ItemsSource = await FluentDL.Services.DeezerApi.SearchTrack(artistName, trackName);
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

    private void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PreviewArtistText.Text = "";
            PreviewTitleText.Text = "";
            PreviewLinkText.Text = "";
            return;
        }

        NoneSelectedText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        // PreviewTitleText.Text = selectedSong.Title + " " + selectedSong.Artists + " " + selectedSong.Link;
        PreviewArtistText.Text = selectedSong.Artists;
        PreviewTitleText.Text = selectedSong.Title;
        PreviewLinkText.Text = selectedSong.Link;
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string selection = e.AddedItems[0].ToString();
        switch (selection)
        {
        }
    }
}