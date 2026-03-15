using FluentDL.Models;
using FluentDL.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentDL.Views
{
    public sealed partial class SpectrogramDialog : UserControl
    {
        private int _isApplyingZoom = 0;
        public SpectrogramDialog()
        {
            InitializeComponent();
            SpectrogramScrollView.SizeChanged += async (s, e) => await SetZoomDefault();
        }

        public async Task OpenSpectrogramDialog(SongSearchObject? selectedSong, DispatcherQueue dispatcher, XamlRoot xamlRoot)
        {
            if (selectedSong != null)
            {
                dispatcher.TryEnqueue(async () =>
                {
                    Dialog.XamlRoot = xamlRoot;
                    SpectrogramImage.Visibility = Visibility.Collapsed;
                    SpectrogramProgressRing.Visibility = Visibility.Visible;
                    SpectrogramProgressRing.IsActive = true;
                    SpectrogramInfoText.Text = "Generating ...";
                    await Dialog.ShowAsync();
                });

                _ = Task.Run(async () =>
                {
                    var bitmapImage = await FFmpegRunner.GetSpectrogram(selectedSong.Id, dispatcher);
                    dispatcher.TryEnqueue(() => {
                        SpectrogramImage.Visibility = Visibility.Visible;
                        SpectrogramProgressRing.Visibility = Visibility.Collapsed;
                        SpectrogramProgressRing.IsActive = false;
                        SpectrogramInfoText.Text = selectedSong.Id;
                        SpectrogramImage.Source = bitmapImage;
                    });
                });
            }
        }
        public async Task SetZoomDefault()
        {
            try
            {
                var measuredHeight = SpectrogramScrollView.ActualHeight;
                var measuredWidth = SpectrogramScrollView.ActualWidth;

                double vpH = measuredHeight;
                double vpW = measuredWidth;
                var imgW = (SpectrogramImage.Source as BitmapImage)?.PixelWidth;
                var imgH = (SpectrogramImage.Source as BitmapImage)?.PixelHeight;
                if (imgH != null && imgW != null)
                {
                    if (Interlocked.CompareExchange(ref _isApplyingZoom, 1, 0) != 0) return;

                    double heightRatio = measuredHeight / measuredWidth;  // Height = X * Width
                    double imgTargetH = Math.Floor(heightRatio * (double)imgW) - 1;
                    double scale_factor = imgTargetH / (double)imgH;
                    SpectrogramScrollView.ZoomTo((float)scale_factor, null);
                    await Task.Delay(100);
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set zoom: {ex.Message}");
                SpectrogramScrollView.ZoomTo(1f, null);
            }
            finally
            {
                Interlocked.Exchange(ref _isApplyingZoom, 0);
            }
        }

        private async void SpectrogramDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            await SetZoomDefault();
        }
    }
}
