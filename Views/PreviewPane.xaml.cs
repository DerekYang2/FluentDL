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


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentDL.Views
{
    public sealed partial class PreviewPane : UserControl
    {
        SongSearchObject? song = null;

        public PreviewPane()
        {
            this.InitializeComponent();
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
            song = selectedSong;
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
                new TrackDetail { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString() },
                new TrackDetail { Label = "Popularity", Value = selectedSong.Rank },
                new TrackDetail { Label = "Duration", Value = new DurationConverter().Convert(selectedSong.Duration, null, null, null).ToString() },
                new TrackDetail { Label = "Album", Value = selectedSong.AlbumName },
                new TrackDetail { Label = "Track", Value = jsonObject.GetProperty("track_position").ToString() }
            };

            // Set 30 second preview
            SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(jsonObject.GetProperty("preview").ToString()));

            PreviewImage.Source = new BitmapImage(new Uri(jsonObject.GetProperty("album").GetProperty("cover_big").ToString()));
        }

        public SongSearchObject? GetSong()
        {
            return song;
        }
    }
}