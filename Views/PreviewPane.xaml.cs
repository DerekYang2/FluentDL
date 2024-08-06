using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using FluentDL.Helpers;
using FluentDL.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using FluentDL.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using ATL;
using FluentDL.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Shapes;
using Newtonsoft.Json.Linq;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentDL.Views
{
    public sealed partial class PreviewPane : UserControl
    {
        SongSearchObject? song = null;
        DispatcherQueue dispatcher;

        public PreviewPane()
        {
            this.InitializeComponent();
            RelativePreviewPanel.Width = Math.Min(App.MainWindow.Width * 0.4, App.MainWindow.Height * 0.5);
            dispatcher = DispatcherQueue.GetForCurrentThread();
            Clear();
        }

        public void SetAppBarButtons(List<AppBarButton> appBarButtons)
        {
            CommandBar.PrimaryCommands.Clear();
            foreach (var button in appBarButtons)
            {
                CommandBar.PrimaryCommands.Add(button);
            }
        }

        public void Clear()
        {
            NoneSelectedText.Visibility = Visibility.Visible;
            SongPreviewPlayer.Visibility = Visibility.Collapsed;
            CommandBar.Visibility = Visibility.Collapsed;
            PreviewScrollView.Visibility = Visibility.Collapsed;
            PreviewTitleText.Text = "";
            PreviewImage.Source = null;
            PreviewInfoControl.ItemsSource = new List<TrackDetail>();
            PreviewInfoControl2.ItemsSource = new List<TrackDetail>();
        }

        public void Show()
        {
            NoneSelectedText.Visibility = Visibility.Collapsed;
            SongPreviewPlayer.Visibility = Visibility.Visible;
            CommandBar.Visibility = Visibility.Visible;
            PreviewScrollView.Visibility = Visibility.Visible;
        }

        public async Task Update(SongSearchObject selectedSong)
        {
            SongPreviewPlayer.Source = null; // Clear previous source
            song = selectedSong;

            //PreviewArtistText.Text = selectedSong.Artists;
            //PreviewReleaseDate.Text = selectedSong.ReleaseDate; // Todo format date
            //PreviewRank.Text = selectedSong.Rank; // Todo format rank
            //PreviewDuration.Text = selectedSong.Duration;
            // PreviewAlbumName.Text = jsonObject.GetProperty("album").GetProperty("title").GetString();
            // PreviewAlbumPosition.Text = jsonObject.GetProperty("track_position").ToString();
            PreviewTitleText.Text = selectedSong.Title;

            var trackDetailsList = new List<TrackDetail>
            {
                new TrackDetail { Label = "Contributing Artists", Value = selectedSong.Artists },
                new TrackDetail { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString() },
                new TrackDetail { Label = "Popularity", Value = selectedSong.Rank },
                new TrackDetail { Label = "Length", Value = new DurationConverter().Convert(selectedSong.Duration, null, null, null).ToString() },
                new TrackDetail { Label = "Album", Value = selectedSong.AlbumName },
            };


            if (selectedSong.Source.Equals("deezer"))
            {
                var jsonObject = await FluentDL.Services.DeezerApi.FetchJsonElement("track/" + selectedSong.Id);
                trackDetailsList.Add(new TrackDetail { Label = "Track", Value = jsonObject.GetProperty("track_position").ToString() });

                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set the details list

                PreviewImage.Source = await ApiHelper.GetBitmapImageAsync(jsonObject.GetProperty("album").GetProperty("cover_big").ToString());
                // Load the audio stream
                var previewUri = jsonObject.GetProperty("preview").ToString();
                if (!string.IsNullOrWhiteSpace(previewUri)) // Some tracks don't have a preview
                {
                    SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(previewUri));
                }
            }

            if (selectedSong.Source.Equals("qobuz"))
            {
                var track = QobuzApi.GetQobuzTrack(selectedSong.Id);

                trackDetailsList.RemoveAt(trackDetailsList.FindIndex(x => x.Label == "Popularity")); // Remove popularity
                trackDetailsList.Add(new TrackDetail() { Label = "Track", Value = selectedSong.TrackPosition });
                trackDetailsList.Add(new TrackDetail { Label = "Performers", Value = track.Performers });
                PreviewImage.Source = new BitmapImage(new Uri(track.Album.Image.Large)); // Get cover art


                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set the details list

                PreviewImage.Source = await ApiHelper.GetBitmapImageAsync(track.Album.Image.Large);
                // Load the audio stream
                SongPreviewPlayer.Source = MediaSource.CreateFromUri(QobuzApi.GetPreviewUri(selectedSong.Id));
            }

            if (selectedSong.Source.Equals("spotify"))
            {
                var track = await SpotifyApi.GetTrack(selectedSong.Id);
                trackDetailsList.Add(new TrackDetail { Label = "Track", Value = selectedSong.TrackPosition });

                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set the details list
                PreviewImage.Source = await ApiHelper.GetBitmapImageAsync(track.Album.Images[0].Url); // Get the largest

                // Load the audio stream
                var previewURL = track.PreviewUrl;
                if (!string.IsNullOrWhiteSpace(previewURL))
                {
                    SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(previewURL));
                }
            }

            if (selectedSong.Source.Equals("youtube"))
            {
                int index = trackDetailsList.FindIndex(x => x.Label == "Popularity"); // Rename popularity to views
                trackDetailsList[index].Label = "Views";

                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set details list
                PreviewImage.Source = await ApiHelper.GetBitmapImageAsync(await YoutubeApi.GetMaxResThumbnail(selectedSong)); // Get max res thumbnail

                // Load the audio stream
                var opusStreamUrl = await YoutubeApi.AudioStreamWorstUrl("https://www.youtube.com/watch?v=" + selectedSong.Id);
                SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(opusStreamUrl));
            }

            if (selectedSong.Source.Equals("local"))
            {
                var track = new Track(selectedSong.Id); // Create track object from file path (id)
                trackDetailsList = new List<TrackDetail>
                {
                    new() { Label = "Contributing Artists", Value = selectedSong.Artists },
                    new() { Label = "Album", Value = selectedSong.AlbumName },
                    new() { Label = "Album Artist", Value = track.AlbumArtist ?? track.Artist ?? "N/A" },
                    new() { Label = "Genre", Value = (track.Genre ?? "N/A").Replace(";", ", ") },
                    new() { Label = "Length", Value = new DurationConverter().Convert(selectedSong.Duration, null, null, null).ToString() },
                    new() { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString() },
                    new() { Label = "Track Position", Value = selectedSong.TrackPosition },
                    new() { Label = "Type", Value = track.AudioFormat.ShortName },
                    new() { Label = "Size", Value = GetFileLength(selectedSong.Id) },
                    new() { Label = "Bit rate", Value = track.Bitrate + " kbps" },
                    new() { Label = "Channels", Value = track.ChannelsArrangement.Description },
                    new() { Label = "Sample rate", Value = track.SampleRate + " Hz" },
                    new() { Label = "Bit depth", Value = track.BitDepth + " bit" },
                };
                SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(selectedSong.Id));
                PreviewImage.Source = await LocalExplorerViewModel.GetBitmapImageAsync(track);

                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // Finally set the details list
            }
        }

        public void ClearMediaPlayerSource()
        {
            SongPreviewPlayer.Source = null;
        }

        public SongSearchObject? GetSong()
        {
            return song;
        }

        public BitmapImage? GetImage()
        {
            return (BitmapImage)PreviewImage.Source;
        }

        private string GetFileLength(string filePath)
        {
            long input = new FileInfo(filePath).Length;
            string output;

            switch (input.ToString().Length)
            {
                case > 12:
                    output = string.Format("{0:F1} TB", input / 1000000000000.0);
                    break;
                case > 9:
                    output = string.Format("{0:F1} GB", input / 1000000000.0);
                    break;
                case > 6:
                    output = string.Format("{0:F1} MB", input / 1000000.0);
                    break;
                case > 3:
                    output = string.Format("{0:F1} KB", input / 1000.0);
                    break;
                default:
                    output = string.Format("{0} B", input);
                    break;
            }

            return output;
        }
    }
}