using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentDL.Views
{
    public sealed partial class SpectrogramDialog : UserControl
    {
        private bool _isOpen;
        private string? _selectedFilePath;

        public SpectrogramDialog()
        {
            InitializeComponent();
        }

        public async Task OpenSpectrogramDialog(SongSearchObject? selectedSong, DispatcherQueue dispatcher, XamlRoot xamlRoot)
        {
            if (selectedSong != null)
            {
                dispatcher.TryEnqueue(async () =>
                {
                    Dialog.XamlRoot = xamlRoot;
                    _selectedFilePath = selectedSong.Id;
                    Dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(_selectedFilePath);
                    SpectrogramImage.Visibility = Visibility.Collapsed;
                    SpectrogramProgressRing.Visibility = Visibility.Visible;
                    SpectrogramProgressRing.IsActive = true;
                    SpectrogramInfoText.Text = "Generating ...";
                    await Dialog.ShowAsync();
                });

                _ = Task.Run(async () =>
                {
                    var bitmapImage = await FFmpegRunner.GetSpectrogram(selectedSong.Id, 512, dispatcher);
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
        private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            _isOpen = true;
            ResetZoom();
        }

        private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            _isOpen = false;
        }

        private void SpectrogramScrollView_SizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (_isOpen)
            {
                Log.Debug(
                    "Spectrogram resized; control {ControlWidth}x{ControlHeight}, image {ImageWidth}x{ImageHeight}, zoom {ZoomFactor}",
                    args.NewSize.Width, args.NewSize.Height,
                    SpectrogramImage.ActualWidth, SpectrogramImage.ActualHeight,
                    SpectrogramScrollView.ZoomFactor);
            }
        }

        private void ResetZoom()
        {
            if (Math.Abs(SpectrogramScrollView.ZoomFactor - 1f) < 0.0001f) return;

            Log.Debug("Resetting spectrogram zoom from {ZoomFactor} to 1", SpectrogramScrollView.ZoomFactor);
            SpectrogramScrollView.ZoomTo(1f, null,
                new ScrollingZoomOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
        }

        private void SpectrogramDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ResetZoom();
        }

        private async void SpectrogramDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;

            if (string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                SpectrogramInfoText.Text = "No audio source selected";
                return;
            }

            var suggestedName = $"{Path.GetFileNameWithoutExtension(_selectedFilePath)}_spectrogram";
            var file = await StoragePickerHelper.FileSavePickerAsync([".png"], suggestedName);
            if (file == null)
            {
                return;
            }

            SpectrogramProgressRing.Visibility = Visibility.Visible;
            SpectrogramProgressRing.IsActive = true;

            try
            {
                var saved = await FFmpegRunner.SaveSpectrogram(_selectedFilePath, file.Path);
                SpectrogramInfoText.Text = saved ? $"Saved: {file.Name}" : "Failed to save spectrogram";
            }
            finally
            {
                SpectrogramProgressRing.Visibility = Visibility.Collapsed;
                SpectrogramProgressRing.IsActive = false;
            }
        }
    }
}
