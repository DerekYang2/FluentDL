using System.Diagnostics;
using System.Text.Json;
using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using FluentDL.Services;

namespace FluentDL.Views;

public sealed partial class BlankPage : Page
{
    private RipSubprocess ripSubprocess;
    public BlankViewModel ViewModel
    {
        get;
    }

    public BlankPage()
    {
        ViewModel = App.GetService<BlankViewModel>();
        InitializeComponent();
        ripSubprocess = new RipSubprocess();
    }

    private async void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        testButton.Content = "Clicked";

        var artistName = artistNameInput.Text;
        var trackName = trackNameInput.Text;

        var req = "search?q=artist:%22" + artistName + "%22%20track:%22" + trackName + "%22";

        // Send a GET request to the Deezer API
        var client = new RestClient("https://api.deezer.com");
        var request = new RestRequest(req);
        // Output the response to the console
        var response = await client.GetAsync(request);
        Debug.WriteLine(response.Content);

        // Create json object from the response
        // Use System.Text.Json to parse the json object
        var jsonObject = JsonDocument.Parse(response.Content).RootElement;
        var debugText = "";
        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            Debug.WriteLine("Track Link: " + track.GetProperty("link").GetString());
            Debug.WriteLine("Track title: " + track.GetProperty("title").GetString());
            Debug.WriteLine("Artist name: " + track.GetProperty("artist").GetProperty("name").GetString());
            debugText += "Track Link: " + track.GetProperty("link").GetString() + "\n";
            debugText += "Track title: " + track.GetProperty("title").GetString() + "\n";
            debugText += "Artist name: " + track.GetProperty("artist").GetProperty("name").GetString() + "\n";
            debugText += "\n";
        }

        testTextBlock.Text = debugText;

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
