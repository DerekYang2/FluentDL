using System.Diagnostics;
using System.Text.Json;
using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using FluentDL.Services;
using System.Collections.ObjectModel;

namespace FluentDL.Views;
// Sample data object used to populate the collection page.
public class SongSearchObject
{
    public string Title
    {
        get; set;
    }
    public string ImageLocation
    {
        get; set;
    }
    public string Link
    {
        get; set;
    }
    public string Likes
    {
        get; set;
    }
    public string Artists
    {
        get; set;
    }

    public SongSearchObject()
    {
    }

    public static List<SongSearchObject> GetDataObjects(bool includeAllItems = false)
    {
        string[] dummyTexts = new[] {
                @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer id facilisis lectus. Cras nec convallis ante, quis pulvinar tellus. Integer dictum accumsan pulvinar. Pellentesque eget enim sodales sapien vestibulum consequat.",
                @"Nullam eget mattis metus. Donec pharetra, tellus in mattis tincidunt, magna ipsum gravida nibh, vitae lobortis ante odio vel quam.",
                @"Quisque accumsan pretium ligula in faucibus. Mauris sollicitudin augue vitae lorem cursus condimentum quis ac mauris. Pellentesque quis turpis non nunc pretium sagittis. Nulla facilisi. Maecenas eu lectus ante. Proin eleifend vel lectus non tincidunt. Fusce condimentum luctus nisi, in elementum ante tincidunt nec.",
                @"Aenean in nisl at elit venenatis blandit ut vitae lectus. Praesent in sollicitudin nunc. Pellentesque justo augue, pretium at sem lacinia, scelerisque semper erat. Ut cursus tortor at metus lacinia dapibus.",
                @"Ut consequat magna luctus justo egestas vehicula. Integer pharetra risus libero, et posuere justo mattis et.",
                @"Proin malesuada, libero vitae aliquam venenatis, diam est faucibus felis, vitae efficitur erat nunc non mauris. Suspendisse at sodales erat.",
                @"Aenean vulputate, turpis non tincidunt ornare, metus est sagittis erat, id lobortis orci odio eget quam. Suspendisse ex purus, lobortis quis suscipit a, volutpat vitae turpis.",
                @"Duis facilisis, quam ut laoreet commodo, elit ex aliquet massa, non varius tellus lectus et nunc. Donec vitae risus ut ante pretium semper. Phasellus consectetur volutpat orci, eu dapibus turpis. Fusce varius sapien eu mattis pharetra.",
            };

        Random rand = new Random();
        int numberOfLocations = includeAllItems ? 13 : 8;
        List<SongSearchObject> objects = new List<SongSearchObject>();
        for (int i = 0; i < numberOfLocations; i++)
        {
            objects.Add(new SongSearchObject()
            {
                Title = $"Item {i + 1}",
                ImageLocation = $"/Assets/SampleMedia/LandscapeImage{i + 1}.jpg",
                Link = rand.Next(100, 999).ToString(),
                Likes = rand.Next(10, 99).ToString(),
                Artists = dummyTexts[i % dummyTexts.Length],
            });
        }

        return objects;
    }

}

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

        List<SongSearchObject> objects = new List<SongSearchObject>();  // Create a list of CustomDataObjects


        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            Debug.WriteLine("Track Link: " + track.GetProperty("link").GetString());
            Debug.WriteLine("Track title: " + track.GetProperty("title").GetString());
            Debug.WriteLine("Artist name: " + track.GetProperty("artist").GetProperty("name").GetString());
            objects.Add(new SongSearchObject()
            {
                Title = track.GetProperty("title").GetString(),
                ImageLocation = track.GetProperty("album").GetProperty("cover").GetString(), // "cover_small, cover_medium, cover_big, cover_xl" are available
                Link = track.GetProperty("link").GetString(), // "link" is the link to the track on Deezer
                Likes = track.GetProperty("rank").ToString(),
                Artists = track.GetProperty("artist").GetProperty("name").GetString()
            });
        }

        // Set the collection as the ItemsSource for the ListView
        CustomListView.ItemsSource = objects;

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
