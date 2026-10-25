using CommunityToolkit.Labs.WinUI.MarqueeTextRns;
using FluentDL.Core.Sources;
using FluentDL.Core.Services;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.ReplayGain;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SpotifyAPI.Web;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Windows.Media.Core;
using Windows.Media.Playback;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentDL.Views
{
    public sealed partial class PreviewPane : UserControl, INotifyPropertyChanged
    {
        private readonly PreviewVolumeSettings previewVolume = App.GetService<PreviewVolumeSettings>();

        private readonly SourceRegistry sources = App.GetService<SourceRegistry>();
        private double rankValue = 0;
        SongSearchObject? song = null;
        DispatcherQueue dispatcher;
        public event PropertyChangedEventHandler? PropertyChanged;

        public double RankValue
        {
            get { return rankValue; }
            set
            {
                rankValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RankValue)));
            }
        }

        private ImageSource? _imageSource;
        public ImageSource? ImageSource
        {
            get => _imageSource;
            set
            {
                _imageSource = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
            }
        }
        // For pre-emption
        private CancellationTokenSource? _previewCts;
        private long _currentRequestId = 0;

        // Streams
        private Stream? _originalStream;
        private Windows.Storage.Streams.IRandomAccessStream? _ras;
        private string? _previewPath;
        private MediaPlayer? activePlayer;
        private bool stackedLayout;
        private double sidePaneWidth;


        private CancellationToken BeginNewRequest()
        {
            // 1. Cancel any existing request currently in-flight
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();

            // 2. Increment request ID so background tasks can check if they are stale
            Interlocked.Increment(ref _currentRequestId);

            // 3. Stop active playback instantly
            ClearMediaPlayerSource();

            return _previewCts.Token;
        }

        public void ClearMediaPlayerSource()
        {
            try
            {
                var mediaPlayer = SongPreviewPlayer.MediaPlayer;
                var source = SongPreviewPlayer.Source;
                if (mediaPlayer == null) // Already cleared
                {
                    return;
                }
                
                mediaPlayer.VolumeChanged -= MediaPlayer_VolumeChanged;
                mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
                if (ReferenceEquals(mediaPlayer, activePlayer))
                {
                    // Capture the last slider value even if its native event is still queued during navigation.
                    _ = SaveVolumeAsync(mediaPlayer.Volume);
                    activePlayer = null;
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

                // Original sources
                _ras?.Dispose();
                _ras = null;

                _originalStream?.Dispose();
                _originalStream = null;

                if (_previewPath != null)
                {
                    File.Delete(_previewPath);
                    _previewPath = null;
                }
            } catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public PreviewPane()
        {
            this.InitializeComponent();
            CleanupTempPreviewFiles(); // Clean up temp opus files on init
            RelativePreviewPanel.Width = Math.Min(App.MainWindow.Width * 0.4, App.MainWindow.Height * 0.5);
            dispatcher = DispatcherQueue.GetForCurrentThread();
            Clear();
            // Set settings
            SongPreviewPlayer.AutoPlay = Task.Run(() => SettingsViewModel.GetSetting<bool>(SettingsViewModel.AutoPlay)).GetAwaiter().GetResult();
        }

        public void SetResponsiveLayout(bool stacked, double availableWidth)
        {
            if (stacked && !stackedLayout) sidePaneWidth = RelativePreviewPanel.Width;
            var width = Math.Clamp(availableWidth - Padding.Left - Padding.Right, 0, 740);
            RelativePreviewPanel.MinWidth = Math.Min(340, width);
            RelativePreviewPanel.MaxWidth = width;
            var preferred = !stacked && stackedLayout ? sidePaneWidth : RelativePreviewPanel.Width;
            RelativePreviewPanel.Width = stacked ? width : Math.Clamp(preferred, RelativePreviewPanel.MinWidth, width);
            PreviewWidthSizer.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
            SongPreviewPlayer.TransportControls.IsCompact = stacked;
            PreviewArtworkColumn.Width = new GridLength(stacked ? 112 : 0);
            PreviewArtwork.MaxHeight = stacked ? 112 : double.PositiveInfinity;
            PreviewArtwork.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            Grid.SetColumnSpan(PreviewArtwork, stacked ? 1 : 2);
            Grid.SetRow(PreviewInfoStack, stacked ? 0 : 1);
            Grid.SetColumn(PreviewInfoStack, stacked ? 1 : 0);
            Grid.SetColumnSpan(PreviewInfoStack, stacked ? 1 : 2);
            PreviewInfoStack.Margin = stacked ? new Thickness(12) : new Thickness(16, 0, 16, 12);
            stackedLayout = stacked;
        }

        public void SetPlayerSource(string? uriStr)
        {
            ClearMediaPlayerSource();
            if (string.IsNullOrWhiteSpace(uriStr))
            {
                return;
            }
            try
            {
                SongPreviewPlayer.Visibility = Visibility.Visible;
                SetMediaSource(MediaSource.CreateFromUri(new Uri(uriStr)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to set media player source: " + ex.Message);
                Serilog.Log.Warning("Could not set preview media source; exception {ExceptionType}, HRESULT {HResult}", ex.GetType().Name, ex.HResult);
            }
        }

        private void SetMediaSource(MediaSource source)
        {
            // Set volume before attaching the source so autoplay cannot start at the default full volume.
            var player = new MediaPlayer { AutoPlay = SongPreviewPlayer.AutoPlay, Volume = previewVolume.Volume };
            SongPreviewPlayer.SetMediaPlayer(player);
            activePlayer = player;
            player.VolumeChanged += MediaPlayer_VolumeChanged;
            player.MediaFailed += MediaPlayer_MediaFailed;
            SongPreviewPlayer.Source = source;
        }

        private void MediaPlayer_VolumeChanged(MediaPlayer sender, object args)
        {
            // Native events may arrive off-thread. Serialize them with navigation and player disposal.
            dispatcher.TryEnqueue(() =>
            {
                if (!ReferenceEquals(sender, activePlayer)) return;
                try { _ = SaveVolumeAsync(sender.Volume); }
                catch (Exception error) { Serilog.Log.Warning(error, "Could not read the preview volume"); }
            });
        }

        private async Task SaveVolumeAsync(double value)
        {
            try
            {
                if (previewVolume.Volume != value)
                    await previewVolume.SetVolumeAsync(value);
            }
            catch (Exception error) { Serilog.Log.Warning(error, "Could not save the shared preview volume"); }
        }

        private void MediaPlayer_MediaFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
        {
            // Do not log the media URI or exception message; signed preview URLs can contain credentials.
            Serilog.Log.Warning("Preview playback failed; error {MediaError}, HRESULT {HResult}", args.Error, args.ExtendedErrorCode?.HResult);
        }

        public void SetPlayerSource(Stream? stream, string? mime)
        {
            ClearMediaPlayerSource();
            if (stream == null || mime == null)
            {
                return;
            }

            // Take ownership
            _originalStream = stream;
            _ras = _originalStream.AsRandomAccessStream();

            SetMediaSource(MediaSource.CreateFromStream(_ras, mime));
            SongPreviewPlayer.Visibility = Visibility.Visible;
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
            BeginNewRequest();  // cancel token
            NoneSelectedText.Visibility = Visibility.Visible;
            SongPreviewPlayer.Visibility = Visibility.Collapsed;
            CommandBar.Visibility = Visibility.Collapsed;
            PreviewScrollView.Visibility = Visibility.Collapsed;
            SongPreviewProgressBar.Visibility = Visibility.Collapsed;
            PreviewTitleText.Text = "";
            ImageSource = null;
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
            var token = BeginNewRequest();
            var requestId = Interlocked.Read(ref _currentRequestId);

            SongPreviewPlayer.AutoPlay = Task.Run(() => SettingsViewModel.GetSetting<bool>(SettingsViewModel.AutoPlay)).GetAwaiter().GetResult(); 
            ClearMediaPlayerSource();
            ImageSource = null; // Clear previous source

            AlbumTrackListContainer.Visibility = selectedSong.GetType() == typeof(AlbumSearchObject) ? Visibility.Visible : Visibility.Collapsed;

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
                new() { Label = "Artists", Value = selectedSong.Artists }, 
                new() { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null!, null!, null!)?.ToString() ?? "" }, 
                new() { Label = "Length", Value = new DurationConverter().Convert(selectedSong.Duration, null!, null!, null!)?.ToString() ?? "" },
                new() { Label = "Album", Value = selectedSong.AlbumName },
            };


            if (sources.TryGet<IPreviewSource>(selectedSong.Source, out var previewSource))
            {
                var info = await previewSource.GetPreviewAsync(selectedSong);
                if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId))
                    return;

                // A source can return an empty or relative cover address; use the row's own image then.
                if (info.CoverUrl != null
                    && (Uri.TryCreate(info.CoverUrl, UriKind.Absolute, out var cover)
                        || Uri.TryCreate(selectedSong.ImageLocation, UriKind.Absolute, out cover)))
                    ImageSource = new BitmapImage(cover);
                if (info.ReleaseDate != null)
                    trackDetailsList.First(t => t.Label == "Release Date").Value = new DateVerboseConverter().Convert(info.ReleaseDate, null!, null!, null!)?.ToString() ?? "";
                if (info.RemoveAlbumRow)
                    trackDetailsList.RemoveAt(trackDetailsList.ToList().FindIndex(t => t.Label == "Album"));
                foreach (var (label, value) in info.Rows)
                    trackDetailsList.Add(new TrackDetail { Label = label, Value = value });
                PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = trackDetailsList;

                if (info.Popularity is { } popularity)
                {
                    trackDetailsList.Add(new TrackDetail { Label = "Popularity", Value = "" });
                    RankRatingControl.Visibility = Visibility.Visible;
                    SetRatingPercentage(popularity);
                }
                else
                {
                    RankRatingControl.Visibility = Visibility.Collapsed;
                }

                if (selectedSong is AlbumSearchObject)
                {
                    SongPreviewPlayer.Visibility = Visibility.Collapsed;
                    TrackListHeader.Text = info.AlbumTrackCount switch
                    {
                        0 => "No Tracks",
                        1 => "1 Track",
                        var count => $"{count} Tracks"
                    };
                    AlbumTrackListView.ItemsSource = info.AlbumTracks ?? [];
                }
                else if (info.LoadAudioSeparately)
                {
                    await LoadPreviewAudioAsync(previewSource, selectedSong, requestId, token);
                }
                else
                {
                    SetPlayerSource(info.AudioUrl);
                }
            }

            if (selectedSong.Source.Equals(SourceIds.Local))
            {
                if (trackInfoObj is MetadataObject metadata)
                {
                    RankRatingControl.Visibility = Visibility.Collapsed;
                    var details = new ObservableCollection<TrackDetail>
                    {
                        new() { Label = "Contributing Artists", Value = selectedSong.Artists },
                        new() { Label = "Album", Value = selectedSong.AlbumName },
                        new() { Label = "Album Artist", Value = string.Join(", ", metadata.AlbumArtists ?? Array.Empty<string>()) },
                        new() { Label = "Genre", Value = string.Join(", ", metadata.Genres ?? Array.Empty<string>()) },
                        new() { Label = "Length", Value = new DurationConverter().Convert(metadata.Duration, null!, null!, null!)?.ToString() ?? "" },
                        new() { Label = "Release Date", Value = new DateVerboseConverter().Convert(selectedSong.ReleaseDate, null!, null!, null!)?.ToString() ?? ""},
                        new() { Label = "Track Position", Value = selectedSong.TrackPosition },
                        new() { Label = "Type", Value = metadata.Codec ?? System.IO.Path.GetExtension(selectedSong.Id) },
                        new() { Label = "Size", Value = GetFileLength(selectedSong.Id) },
                        new() { Label = "Bit rate", Value = metadata.AudioBitrate + " kbps" },
                        new() { Label = "Channels", Value = metadata.AudioChannels.ToString() },
                        new() { Label = "Sample rate", Value = metadata.AudioSampleRate + " Hz" },
                        new() { Label = "Bit depth", Value = metadata.BitsPerSample + " bit" },
                    };
                    AddReplayGainDetails(details, selectedSong.Id);
                    details.Add(new() { Label = "Location", Value = selectedSong.Id });
                    PreviewInfoControl2.ItemsSource = PreviewInfoControl.ItemsSource = details; // Set the details list
                    SetPlayerSource(selectedSong.Id);
                    var byteBuffer = metadata.GetAlbumArt();

                    if (byteBuffer is { Length: > 0 })
                    {
                        var bitmapImage = new BitmapImage();
                        using var stream = new MemoryStream(byteBuffer);
                        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                        if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId))
                            return;
                        ImageSource = bitmapImage;
                    }
                }
                else
                {
                    throw new Exception("Metadata object is null");
                }
            }
        }

        // Read from the file each time, because MetadataObject is cached and ReplayGain tags may have been written since.
        private static void AddReplayGainDetails(ICollection<TrackDetail> details, string path)
        {
            try
            {
                var tags = ReplayGainScanner.ReadTags(path);
                if (tags.TrackGain is { } trackGain) details.Add(new() { Label = "Track gain", Value = ReplayGainMath.FormatGain(trackGain) });
                if (tags.AlbumGain is { } albumGain) details.Add(new() { Label = "Album gain", Value = ReplayGainMath.FormatGain(albumGain) });
            }
            catch (Exception e)
            {
                Serilog.Log.Debug(e, "Could not read ReplayGain tags for the preview");
            }
        }

        public SongSearchObject? GetSong()
        {
            return song;
        }

        public BitmapImage? GetImage()
        {
            return ImageSource as BitmapImage;
        }

        public List<SongSearchObject>? GetAlbumTrackList()
        {
            return AlbumTrackListView.ItemsSource as List<SongSearchObject>;
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

        // percentage between 0 and 1
        private void SetRatingPercentage(double percentage)
        {
            RankValue = percentage * 5;
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

        private async void AlbumTrackListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedSong = (SongSearchObject?)AlbumTrackListView.SelectedItem;
            if (selectedSong == null)
            {
                ClearMediaPlayerSource();
                return; // No song selected
            }
            var token = BeginNewRequest();
            var requestId = Interlocked.Read(ref _currentRequestId);

            if (sources.TryGet<IPreviewSource>(selectedSong.Source, out var previewSource))
            {
                await LoadPreviewAudioAsync(previewSource, selectedSong, requestId, token);
            }
        }

        // YouTube audio may come through yt-dlp (an app setting), so the pane loads it itself.
        private async Task LoadPreviewAudioAsync(IPreviewSource source, SongSearchObject track, long requestId, CancellationToken token)
        {
            if (track.Source == SourceIds.YouTube)
            {
                await LoadYouTubeAudioPreviewAsync(source, track, requestId, token);
                return;
            }
            try
            {
                var previewUri = await source.GetPreviewAudioAsync(track);
                if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId))
                    return;
                SetPlayerSource(previewUri);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId)) return;
                Serilog.Log.Warning("Could not load {Provider} preview; exception {ExceptionType}, HRESULT {HResult}", track.Source, error.GetType().Name, error.HResult);
                ClearMediaPlayerSource();
            }
        }
        
        private async Task LoadYouTubeAudioPreviewAsync(IPreviewSource source, SongSearchObject track, long requestId, CancellationToken token)
        {
            if (track.Id == null) return;
            var youtubeUrl = "https://www.youtube.com/watch?v=" + track.Id;
            
            if (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.UseYtdlp))
            {
                string? previewPath = null;
                try
                {
                    SongPreviewProgressBar.Visibility = Visibility.Visible;

                    previewPath = Path.Combine(Path.GetTempPath(), "FluentDL", $"preview-{Guid.NewGuid():N}.webm");
                    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                    var ytdlpPath = await SettingsViewModel.GetSetting<string?>(SettingsViewModel.YtdlpPath);
                    await YtDlp.DownloadAudioYTDLPWorst(previewPath, youtubeUrl, ytdlpPath, token);
                    if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId))
                        return;
                    _previewPath = previewPath;
                    SetPlayerSource(previewPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed to load YouTube preview with yt-dlp: " + ex.Message);
                } finally
                {
                    SongPreviewProgressBar.Visibility = Visibility.Collapsed;
                    if (File.Exists(previewPath))
                    {
                        File.Delete(previewPath);
                    }
                }
            }
            else
            {
                try
                {
                    SongPreviewProgressBar.Visibility = Visibility.Visible;
                    var previewUri = await source.GetPreviewAudioAsync(track);
                    if (token.IsCancellationRequested || requestId != Interlocked.Read(ref _currentRequestId))
                        return;
                    SetPlayerSource(previewUri);
                } finally
                {
                    SongPreviewProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private static void CleanupTempPreviewFiles()
        {
            try
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), "FluentDL");
                if (Directory.Exists(tempFolder))
                {
                    foreach (var file in Directory.GetFiles(tempFolder, "*.opus"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete temp file {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to cleanup temp preview files: " + ex.Message);
            }
        }
        
        // Fix background acrylic loading
        private CancellationTokenSource _bgRefreshCts;

        private void PreviewImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            _bgRefreshCts?.Cancel();
            _bgRefreshCts = new CancellationTokenSource();

            var token = _bgRefreshCts.Token;
            Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(10);
                double delayTime = 100; // ms
                while (!token.IsCancellationRequested && DateTime.UtcNow - start < timeout)
                {
                    // Ask the UI thread to perform a tiny nudge
                    dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            BackgroundImage.Opacity = 0.999;
                            BackgroundImage.Opacity = 1;
                        }
                        catch { 
                            // Sometimes crashes when force close application, ignore
                        }
                    });

                    // Check less frequently if you want to reduce load
                    await Task.Delay((int)delayTime, token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    // Increase delay time
                    delayTime = Math.Min(delayTime * 1.5, timeout.TotalMilliseconds); // Cap at max time
                }
            }, token);
        }
    }
}
