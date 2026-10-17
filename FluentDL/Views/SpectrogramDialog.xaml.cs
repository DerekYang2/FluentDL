using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using Spek.Net;
using Spek.Net.WinUI;
using Windows.System;

namespace FluentDL.Views;

// Hosts the NuGet control rather than generating and scaling a finished PNG. Closing stops the decoder; image export
// uses the current view's zoom, palette and optional guides without running a second decode.
public sealed partial class SpectrogramDialog : UserControl
{
    private bool isOpen;
    private bool saving;
    private bool completed;
    private bool updatingOptions;
    private string? selectedFilePath;

    public SpectrogramDialog()
    {
        InitializeComponent();
        Spek.AnalysisCompleted += (_, _) =>
        {
            if (!isOpen || string.IsNullOrEmpty(Spek.FilePath)) return;
            completed = true;
            UpdateState();
            Log.Debug("Spectrogram analysis finished for {Path}", selectedFilePath);
        };
        Spek.InspectionChanged += (_, _) => UpdateInspection();
        Spek.RegisterPropertyChangedCallback(SpekView.RefinementStatusProperty, (_, _) => UpdateInspection());
        foreach (var property in new[]
        {
            SpekView.PaletteProperty, SpekView.WindowFunctionProperty, SpekView.FftBitsProperty,
            SpekView.ChannelProperty, SpekView.StreamProperty, SpekView.ChannelCountProperty,
            SpekView.StreamCountProperty, SpekView.DescriptionProperty, SpekView.IsAnalyzingProperty,
            SpekView.LowerDbProperty, SpekView.UpperDbProperty,
        })
        {
            Spek.RegisterPropertyChangedCallback(property, (_, changed) =>
            {
                if (changed == SpekView.IsAnalyzingProperty && Spek.IsAnalyzing) completed = false;
                UpdateState();
            });
        }
        LowerBox.Value = Spek.LowerDb;
        UpperBox.Value = Spek.UpperDb;
        ReferencesBox.SelectedIndex = 0;
    }

    public async Task OpenSpectrogramDialog(SongSearchObject? selectedSong, XamlRoot xamlRoot)
    {
        if (isOpen || selectedSong is null || selectedSong.Source != SourceIds.Local) return;
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Open the spectrogram on the UI thread.");

        isOpen = true;
        completed = false;
        Message.IsOpen = false;
        selectedFilePath = selectedSong.Id;
        try
        {
            Dialog.XamlRoot = xamlRoot;
            ResizeForRoot();
            xamlRoot.Changed += Root_Changed;
            Spek.FfmpegPath = Path.Combine(await FFmpegRunner.GetPath(), "ffmpeg.exe");
            Spek.ResetZoom();
            Spek.ClearSelection();
            Spek.FilePath = selectedFilePath;
            AnalysisStatus.Text = "Reading file information...";
            UpdateState();
            await Dialog.ShowAsync();
        }
        finally
        {
            isOpen = false;
            xamlRoot.Changed -= Root_Changed;
            // Also cover a failed ShowAsync or page teardown. The control cancels work on unload too.
            Spek.FilePath = null;
            completed = false;
            Dialog.IsPrimaryButtonEnabled = false;
        }
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) => Spek.Focus(FocusState.Programmatic);

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.None) Spek.FilePath = null;
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeForRoot();

    private void ResizeForRoot()
    {
        if (Dialog.XamlRoot is not { } root) return;
        DialogBody.Width = Math.Max(0, Math.Min(1280, root.Size.Width - 96));
        DialogBody.Height = Math.Max(0, Math.Min(760, root.Size.Height - 208));
        Profile.Height = DialogBody.Height < 600 ? 120 : 160;
    }

    private void Spek_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        // SpekView consumes Escape to clear selection. Let the next Escape close the host dialog normally.
        if (args.Key == VirtualKey.Escape && !Spek.HasSelection)
        {
            Dialog.Hide();
            args.Handled = true;
        }
    }

    private void ResetZoom_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        Spek.ResetZoom();
        Spek.Focus(FocusState.Programmatic);
    }

    private async void SaveImage_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (saving || !completed || Spek.IsAnalyzing || string.IsNullOrWhiteSpace(selectedFilePath)) return;
        var deferral = args.GetDeferral();
        saving = true;
        Message.IsOpen = false;
        UpdateState();
        try
        {
            var file = await StoragePickerHelper.FileSavePickerAsync([".png"], $"{Path.GetFileNameWithoutExtension(selectedFilePath)}_spectrogram");
            if (file is null || !isOpen) return;
            await Spek.SaveAsync(file.Path);
            Message.Severity = InfoBarSeverity.Success;
            Message.Message = $"Saved {file.Name}";
            Message.IsOpen = true;
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not export the spectrogram for {Path}", selectedFilePath);
            Message.Severity = InfoBarSeverity.Error;
            Message.Message = "The spectrogram could not be saved. Check the destination and try again.";
            Message.IsOpen = true;
        }
        finally
        {
            saving = false;
            UpdateState();
            if (isOpen) Spek.Focus(FocusState.Programmatic);
            deferral.Complete();
        }
    }

    private void UpdateState()
    {
        PaletteButton.Content = Palettes.DisplayName(Spek.Palette);
        WindowButton.Content = WindowFunctions.DisplayName(Spek.WindowFunction);
        FftButton.Content = $"{1 << Spek.FftBits} samples";
        ChannelButton.Content = Spek.ChannelCount > 0 ? $"Channel {Spek.Channel + 1} / {Spek.ChannelCount}" : "Channel";
        ChannelButton.IsEnabled = Spek.ChannelCount > 1;
        StreamButton.Content = $"Stream {Spek.Stream + 1} / {Spek.StreamCount}";
        StreamButton.Visibility = Spek.StreamCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        Dialog.IsPrimaryButtonEnabled = isOpen && completed && !Spek.IsAnalyzing && !saving;
        Dialog.IsSecondaryButtonEnabled = !saving;
        if (isOpen)
            AnalysisStatus.Text = saving ? "Saving image..." : Spek.IsAnalyzing ? "Analyzing..." : completed ? "Ready" :
                string.IsNullOrEmpty(Spek.Description) ? "Reading file information..." : Spek.Description;

        updatingOptions = true;
        LowerBox.Value = Spek.LowerDb;
        UpperBox.Value = Spek.UpperDb;
        updatingOptions = false;
    }

    private void UpdateInspection()
    {
        Profile.Visibility = Spek.ShowSpectrum ? Visibility.Visible : Visibility.Collapsed;
        Profile.Profile = Spek.SpectrumProfile;
        Profile.FrequencyRange = Spek.VisibleFrequencyRange;
        Profile.MeasuredDrop = Spek.ShowCutoffMarker ? Spek.MeasuredDrop : null;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Spek.RefinementStatus)) parts.Add(Spek.RefinementStatus);
        var time = Spek.VisibleTimeRange;
        if (Spek.MinimumTime > 0 || Spek.MaximumTime > 0)
            parts.Add($"Time view {time.Start:0.00} to {time.End:0.00} s.");
        var range = Spek.VisibleFrequencyRange;
        if (Spek.MinimumFrequency > 0 || Spek.MaximumFrequency > 0)
            parts.Add($"Frequency view {range.Minimum / 1000:0.00} to {range.Maximum / 1000:0.00} kHz.");
        if (Spek.HasSelection) parts.Add($"Passage {Spek.SelectionStartSeconds:0.00} to {Spek.SelectionEndSeconds:0.00} s.");
        else if (Spek.ShowSpectrum || Spek.ShowCutoffMarker) parts.Add("Whole file selected. Drag to inspect a passage.");
        if (Spek.ReferenceLines == ReferenceLines.Mp3Lame) parts.Add("Guides are nominal LAME stereo CBR targets, not bitrate detection.");
        if (Spek.ShowCutoffMarker)
        {
            parts.Add(Spek.MeasuredDrop is { } drop ? $"Spectral drop near {drop.FrequencyHz / 1000:0.00} kHz." :
                Spek.SpectrumProfile is null ? "Cutoff measurement waits for analysis." : "No qualifying spectral drop found.");
            parts.Add("A cutoff does not prove a lossy source, and no marker does not prove lossless origin.");
        }
        InspectionStatus.Text = string.Join(" ", parts);
    }

    private void Range_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (updatingOptions || Spek is null || !double.IsFinite(LowerBox.Value) || !double.IsFinite(UpperBox.Value)) return;
        int lower = Math.Clamp((int)LowerBox.Value, SpekDefaults.MinDb, -1);
        int upper = Math.Clamp((int)UpperBox.Value, SpekDefaults.MinDb + 1, 0);
        if (lower >= upper)
        {
            Message.Severity = InfoBarSeverity.Warning;
            Message.Message = "The lower dB limit must be below the upper limit.";
            Message.IsOpen = true;
            return;
        }
        Message.IsOpen = false;
        Spek.LowerDb = lower;
        Spek.UpperDb = upper;
    }

    private void References_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (Spek is null || ReferencesBox.SelectedIndex < 0) return;
        Spek.ReferenceLines = (ReferenceLines)ReferencesBox.SelectedIndex;
        UpdateInspection();
    }

    private void Inspection_Changed(object sender, RoutedEventArgs args)
    {
        if (Spek is null) return;
        Spek.ShowSpectrum = ProfileToggle.IsChecked == true;
        Spek.ShowCutoffMarker = CutoffToggle.IsChecked == true;
        UpdateInspection();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs args)
    {
        Spek.ClearSelection();
        Spek.Focus(FocusState.Programmatic);
    }

    private void OptionsMenu_Opening(object sender, object args)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();
        if (menu == PaletteMenu)
            foreach (var palette in Enum.GetValues<Palette>()) Add(Palettes.DisplayName(palette), Spek.Palette == palette, () => Spek.Palette = palette);
        else if (menu == WindowMenu)
            foreach (var function in Enum.GetValues<WindowFunction>()) Add(WindowFunctions.DisplayName(function), Spek.WindowFunction == function, () => Spek.WindowFunction = function);
        else if (menu == FftMenu)
            for (int bits = SpekDefaults.MinFftBits; bits <= SpekDefaults.MaxFftBits; bits++)
            {
                int value = bits;
                Add($"{1 << bits} samples", Spek.FftBits == bits, () => Spek.FftBits = value);
            }
        else if (menu == ChannelMenu)
            for (int channel = 0; channel < Spek.ChannelCount; channel++)
            {
                int value = channel;
                Add($"Channel {channel + 1}", Spek.Channel == channel, () => Spek.Channel = value);
            }
        else if (menu == StreamMenu)
            for (int stream = 0; stream < Spek.StreamCount; stream++)
            {
                int value = stream;
                Add($"Stream {stream + 1}", Spek.Stream == stream, () => Spek.Stream = value);
            }

        void Add(string text, bool selected, Action select)
        {
            var item = new RadioMenuFlyoutItem { Text = text, IsChecked = selected, GroupName = menu.GetHashCode().ToString() };
            item.Click += (_, _) => { select(); Spek.Focus(FocusState.Programmatic); };
            menu.Items.Add(item);
        }
    }
}
