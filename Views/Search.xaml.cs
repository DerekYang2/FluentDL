using System.Diagnostics;
using System.Text.Json;
using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using FluentDL.Services;
using System.Collections.ObjectModel;

namespace FluentDL.Views;
// TODO: loading for search

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
    }

    private async void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var artistName = artistNameInput.Text;
        var trackName = trackNameInput.Text;
        SearchProgress.IsIndeterminate = true;
        // Set the collection as the ItemsSource for the ListView
        CustomListView.ItemsSource = await FluentDL.Services.DeezerApi.SearchTrack(artistName, trackName);
        SearchProgress.IsIndeterminate = false;
        Debug.WriteLine(ripSubprocess.RunCommandSync("rip config path"));

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
}