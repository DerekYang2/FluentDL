using System.Collections.ObjectModel;
using System.Diagnostics;
using FluentDL.Helpers;
using FluentDL.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using FluentDL.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using CommunityToolkit.Labs.WinUI.MarqueeTextRns;
using FluentDL.ViewModels;


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
            SongPreviewPlayer.AutoPlay = Task.Run(() => SettingsViewModel.GetSetting<bool>(SettingsViewModel.AutoPlay)).GetAwaiter().GetResult(); 
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
            ClearMediaPlayerSource();
        }

        public void Show()
        {
            NoneSelectedText.Visibility = Visibility.Collapsed;
            SongPreviewPlayer.Visibility = Visibility.Visible;
            CommandBar.Visibility = Visibility.Visible;
            PreviewScrollView.Visibility = Visibility.Visible;
        }

        public async Task Update(SongSearchObject selectedSong, object? trackInfoObj = null)
        {
            SongPreviewPlayer.AutoPlay = Task.Run(() => SettingsViewModel.GetSetting<bool>(SettingsViewModel.AutoPlay)).GetAwaiter().GetResult(); 

            ClearMediaPlayerSource();
            PreviewImage.Source = null; // Clear previous source
            PreviewImage.UpdateLayout();

            song = selectedSong;

            //PreviewArtistText.Text = selectedSong.Artists;
            //PreviewReleaseDate.Text = selectedSong.ReleaseDate; // Todo format date
            //PreviewRank.Text = selectedSong.Rank; // Todo format rank
            //PreviewDuration.Text = selectedSong.Duration;
            // PreviewAlbumName.Text = jsonObject.GetProperty("album").GetProperty("title").GetString();
            // PreviewAlbumPosition.Text = jsonObject.GetProperty("track_position").ToString();
            PreviewTitleText.Text = selectedSong.Title;

            var trackDetailsList = new ObservableCollection<TrackDetail>
            {
                new TrackDetail { Label = "Artists", Value = selectedSong.Artists }, new TrackDetail { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString() }, new TrackDetail { Label = "Length", Value = new DurationConverter().Convert(selectedSong.Duration, null, null, null).ToString() }, new TrackDetail { Label = "Album", Value = selectedSong.AlbumName },
            };


            if (selectedSong.Source.Equals("deezer"))
            {
                var jsonObject = await FluentDL.Services.DeezerApi.FetchJsonElement("track/" + selectedSong.Id);
                var albumObj = jsonObject.GetProperty("album");
                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set the details list
                PreviewImage.Source = new BitmapImage(new Uri(albumObj.GetProperty("cover_big").ToString()));

                trackDetailsList.Add(new TrackDetail { Label = "Track", Value = jsonObject.GetProperty("track_position").ToString() });
                trackDetailsList.Add(new TrackDetail { Label = "Genre", Value = await DeezerApi.GetGenreStr(albumObj.GetProperty("id").GetInt32()) });
                trackDetailsList.Add(new TrackDetail { Label = "Max Quality", Value = $"16-Bit/44.1 kHz"});

                trackDetailsList.Add(new TrackDetail { Label = "Popularity", Value = "" });
                SetPopularityIcon(ApiHelper.GetRank(selectedSong));


                // Load the audio stream
                var previewUri = jsonObject.GetProperty("preview").ToString();
                if (!string.IsNullOrWhiteSpace(previewUri)) // Some tracks don't have a preview
                {
                    SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(previewUri));
                }
            }

            if (selectedSong.Source.Equals("qobuz"))
            {
                var track = await QobuzApi.GetInternalTrack(selectedSong.Id);

                PreviewImage.Source = new BitmapImage(new Uri(track.Album.Image.Large)); // Get cover art

                // trackDetailsList.RemoveAt(trackDetailsList.ToList().FindIndex(x => x.Label == "Popularity")); // Remove popularity
                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList;
                trackDetailsList.Add(new TrackDetail() { Label = "Track", Value = selectedSong.TrackPosition });
                trackDetailsList.Add(new TrackDetail { Label = "Genre", Value = string.Join(", ", QobuzApi.PruneGenreList(track.Album.GenresList)) });
                trackDetailsList.Add(new TrackDetail { Label = "Max Quality", Value = $"{track.MaximumBitDepth}-Bit/{track.MaximumSamplingRate} kHz"});
                PopularityIcon.Visibility = Visibility.Collapsed;

                // Load the audio stream
                SongPreviewPlayer.Source = MediaSource.CreateFromUri(QobuzApi.GetPreviewUri(selectedSong.Id));
            }

            if (selectedSong.Source.Equals("spotify"))
            {
                var track = await SpotifyApi.GetTrack(selectedSong.Id);

                PreviewImage.Source = new BitmapImage(new Uri(track.Album.Images[0].Url)); // Get the largest
                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set the details list
                trackDetailsList.Add(new TrackDetail { Label = "Track", Value = selectedSong.TrackPosition });
                var genreStr = string.Join(", ", await SpotifyApi.GetGenres(track.Artists));
                trackDetailsList.Add(new TrackDetail { Label = "Genre", Value = genreStr });
                trackDetailsList.Add(new TrackDetail { Label = "Popularity", Value = "" });
                SetPopularityIcon(ApiHelper.GetRank(selectedSong));

                // Load the audio stream
                var previewURL = track.PreviewUrl;
                if (!string.IsNullOrWhiteSpace(previewURL))
                {
                    SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(previewURL));
                }
            }

            if (selectedSong.Source.Equals("youtube"))
            {
                trackDetailsList.Add(new TrackDetail { Label = "Views", Value = selectedSong.Rank });

                PreviewImage.Source = new BitmapImage(new Uri(await YoutubeApi.GetMaxResThumbnail(selectedSong))); // Get max res thumbnail
                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList; // First set details list
                PopularityIcon.Visibility = Visibility.Collapsed;

                // Load the audio stream
                var opusStreamUrl = await YoutubeApi.AudioStreamWorstUrl("https://www.youtube.com/watch?v=" + selectedSong.Id);
                SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(opusStreamUrl));
            }

            if (selectedSong.Source.Equals("local"))
            {
                if (trackInfoObj is MetadataObject metadata)
                {
                    PopularityIcon.Visibility = Visibility.Collapsed;
                    PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = new ObservableCollection<TrackDetail> // Set the details list
                    {
                        new() { Label = "Contributing Artists", Value = selectedSong.Artists },
                        new() { Label = "Album", Value = selectedSong.AlbumName },
                        new() { Label = "Album Artist", Value = string.Join(", ", metadata.AlbumArtists ?? Array.Empty<string>()) },
                        new() { Label = "Genre", Value = string.Join(", ", metadata.Genres ?? Array.Empty<string>()) },
                        new() { Label = "Length", Value = new DurationConverter().Convert(metadata.Duration, null, null, null).ToString() },
                        new() { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null, null, null).ToString() },
                        new() { Label = "Track Position", Value = selectedSong.TrackPosition },
                        new() { Label = "Type", Value = metadata.Codec ?? System.IO.Path.GetExtension(selectedSong.Id) },
                        new() { Label = "Size", Value = GetFileLength(selectedSong.Id) },
                        new() { Label = "Bit rate", Value = metadata.tfile.Properties.AudioBitrate + " kbps" },
                        new() { Label = "Channels", Value = metadata.tfile.Properties.AudioChannels.ToString() },
                        new() { Label = "Sample rate", Value = metadata.tfile.Properties.AudioSampleRate + " Hz" },
                        new() { Label = "Bit depth", Value = metadata.tfile.Properties.BitsPerSample + " bit" },
                        new() { Label = "Location", Value = selectedSong.Id }
                    };

                    SongPreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(selectedSong.Id));
                    var byteBuffer = metadata.GetAlbumArt();

                    if (byteBuffer is { Length: > 0 })
                    {
                        var bitmapImage = new BitmapImage();
                        using var stream = new MemoryStream(byteBuffer);
                        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                        PreviewImage.Source = bitmapImage;
                    }
                }
                else
                {
                    throw new Exception("Metadata object is null");
                }
            }
        }

        public void ClearMediaPlayerSource()
        {
            var mediaPlayer = SongPreviewPlayer.MediaPlayer;
            var source = SongPreviewPlayer.Source;
            if (mediaPlayer == null || source == null) // Already cleared
            {
                return;
            }

            if (mediaPlayer.PlaybackSession.CanPause)
            {
                mediaPlayer.Pause();
            }

            SongPreviewPlayer.Source = null;
            SongPreviewPlayer.SetMediaPlayer(null);
            if (source is MediaSource mediaSource)
            {
                mediaSource.Dispose();
            }

            mediaPlayer.Dispose();
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
            try
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
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return "Unknown";
            }
        }

        // popVal is between 1 and 5
        private void SetPopularityIcon(int popVal)
        {
            if (popVal == 0)
            {
                PopularityIcon.Visibility = Visibility.Collapsed;
                return;
            }

            PopularityIcon.Visibility = Visibility.Visible;
            PopularityIcon.Glyph = popVal switch
            {
                1 => "\uE904",
                2 => "\uE905",
                3 => "\uE906",
                4 => "\uE907",
                5 => "\uE908",
                _ => "\uE904"
            };
        }

        private void PreviewMarquee_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var MarqueeControl = sender as MarqueeText;
            if (MarqueeControl == null)
            {
                Debug.WriteLine("Marquee control is null");
                return;
            }

            MarqueeControl.StartMarquee();
        }

        private void PreviewMarquee_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            var MarqueeControl = sender as MarqueeText;
            if (MarqueeControl == null)
            {
                Debug.WriteLine("Marquee control is null");
                return;
            }

            MarqueeControl.StopMarquee();
        }

        private void PreviewMarquee_OnLoaded(object sender, RoutedEventArgs e)
        {
            var MarqueeControl = sender as MarqueeText;
            if (MarqueeControl == null)
            {
                Debug.WriteLine("Marquee control is null");
                return;
            }

            MarqueeControl.StopMarquee(); // Initialize marquee as stopped
        }
    }
}