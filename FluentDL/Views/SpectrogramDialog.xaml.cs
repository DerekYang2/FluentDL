using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Serilog;
using Spek.Net;
using Spek.Net.WinUI;
using Windows.System;
using Windows.UI.Core;

namespace FluentDL.Views;

// Hosts the NuGet control rather than generating and scaling a finished PNG. Closing stops the decoder; image export
// uses the current view's zoom, palette and optional guides without running a second decode.
public sealed partial class SpectrogramDialog : UserControl
{
    private bool isOpen;
    private bool saving;
    private bool completed;
    private bool updatingOptions;
    private bool escapeClearedSelection;
    private string? selectedFilePath;

    public SpectrogramDialog()
    {
        InitializeComponent();
        DialogBody.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(DialogBody_KeyUp), handledEventsToo: true);
        Spek.AnalysisCompleted += (_, _) =>
        {
            if (!isOpen || string.IsNullOrEmpty(Spek.FilePath)) return;
            completed = true;
            UpdateState();
            Log.Debug("Spectrogram analysis finished for {Path}", selectedFilePath);
        };
        Spek.InspectionChanged += (_, _) => UpdateInspection();
        Spek.RegisterPropertyChangedCallback(SpekView.RefinementStatusProperty, (_, _) => UpdateInspection());
        Overview.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateNavigationLayout());
        NavigationContent.SizeChanged += (_, _) => UpdateNavigationLayout();
        ClearSelectionButton.SizeChanged += (_, _) => UpdateNavigationLayout();
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
        escapeClearedSelection = false;
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

    private void Dialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // WinUI invokes the close button for Escape too. Cancel only that keyboard action when it clears a passage;
        // mouse, touch and accessibility activation of Close should always dismiss the dialog.
        bool escapePressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Escape).HasFlag(CoreVirtualKeyStates.Down);
        if (escapePressed && (escapeClearedSelection || Spek.HasSelection))
        {
            Spek.ClearSelection();
            args.Cancel = true;
        }
        escapeClearedSelection = false;
    }

    private void DialogBody_KeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape) escapeClearedSelection = false;
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeForRoot();

    private void ResizeForRoot()
    {
        if (Dialog.XamlRoot is not { } root) return;
        DialogBody.Width = Math.Max(0, Math.Min(1280, root.Size.Width - 96));
        DialogBody.Height = Math.Max(0, Math.Min(760, root.Size.Height - 208));
        Profile.Height = DialogBody.Height < 600 ? 120 : 160;
        UpdateNavigationLayout();
    }

    private void DialogBody_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        escapeClearedSelection = false;
        if (Spek.HasSelection)
        {
            escapeClearedSelection = true;
            Spek.ClearSelection();
        }
        else
        {
            Dialog.Hide();
        }
        args.Handled = true;
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs args)
    {
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
        ResetZoomButton.IsEnabled = !saving;
        UpdateAnalysisStatus();

        updatingOptions = true;
        LowerBox.Value = Spek.LowerDb;
        UpperBox.Value = Spek.UpperDb;
        updatingOptions = false;
    }

    private void UpdateAnalysisStatus()
    {
        SaveStatus.Visibility = isOpen && saving ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateInspection()
    {
        double x = Math.Round(Spek.TimeZoomFactor * 100, 2);
        double y = Math.Round(Spek.FrequencyZoomFactor * 100, 2);
        ZoomReadout.Text = x == y ? $"Zoom: {x:0.##}%" : $"X: {x:0.##}% / Y: {y:0.##}%";
        Profile.Visibility = Spek.ShowSpectrum ? Visibility.Visible : Visibility.Collapsed;
        Profile.Caption = Spek.HasSelection
            ? $"Average spectral level of the selected passage, {Spek.SelectionStartSeconds:0.00} to {Spek.SelectionEndSeconds:0.00} s"
            : "Average spectral level of the whole track";
        Profile.Profile = Spek.SpectrumProfile;
        Profile.FrequencyRange = Spek.VisibleFrequencyRange;
        Profile.MeasuredDrop = Spek.ShowCutoffMarker ? Spek.MeasuredDrop : null;
        bool canClearSelection = Spek.ShowSpectrum && Spek.HasSelection;
        ClearSelectionButton.IsEnabled = canClearSelection;
        ClearSelectionButton.Visibility = canClearSelection ? Visibility.Visible : Visibility.Collapsed;
        var parts = new List<string>();
        if (Spek.RefinementStatus.StartsWith("Zoom refinement failed", StringComparison.Ordinal)) parts.Add(Spek.RefinementStatus);
        if (Spek.ReferenceLines == ReferenceLines.Mp3Lame) parts.Add("Guides are nominal LAME stereo CBR targets, not bitrate detection.");
        if (Spek.ShowCutoffMarker)
        {
            parts.Add(Spek.MeasuredDrop is { } drop ? $"Spectral drop near {drop.FrequencyHz / 1000:0.00} kHz." :
                Spek.SpectrumProfile is null ? "Cutoff measurement waits for analysis." : "No qualifying spectral drop found.");
            parts.Add("A cutoff does not prove a lossy source, and no marker does not prove lossless origin.");
        }
        InspectionStatus.Text = string.Join(" / ", parts);
        InspectionStatus.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigationLayout();
    }

    private void UpdateNavigationLayout()
    {
        double available = DialogBody.Width;
        if (!double.IsFinite(available) || available <= 0) return;
        bool showMap = Overview.Visibility == Visibility.Visible;
        bool showClear = ClearSelectionButton.Visibility == Visibility.Visible;
        double clearWidth = showClear ? Math.Max(112, ClearSelectionButton.DesiredSize.Width) : 0;
        double referenceWidth = ReferencesBox.Width;
        double profileWidth = ProfileToggle.DesiredSize.Width;
        double cutoffWidth = CutoffToggle.DesiredSize.Width;
        double resetWidth = ResetZoomButton.DesiredSize.Width;
        double minimumMapSpace = showMap ? 96 : 0;
        double controlsWidth = referenceWidth + profileWidth + cutoffWidth + resetWidth + 36 + (showClear ? clearWidth + 12 : 0);
        bool wrapReset = controlsWidth + minimumMapSpace > available;
        if (wrapReset) controlsWidth = referenceWidth + Math.Max(profileWidth, resetWidth) + Math.Max(cutoffWidth, clearWidth) + 24;
        bool wrapOptions = wrapReset && controlsWidth + minimumMapSpace > available;
        if (wrapOptions) controlsWidth = Math.Max(referenceWidth, resetWidth) + Math.Max(profileWidth, cutoffWidth) + 12;
        controlsWidth = Math.Max(controlsWidth, ZoomReadout.MaxWidth + 8 + AnalysisStatus.Width);
        Grid.SetRow(ResetZoomButton, wrapReset ? 1 : 0);
        Grid.SetColumn(ResetZoomButton, wrapOptions ? 0 : wrapReset ? 1 : 3);
        Grid.SetRow(CutoffToggle, wrapOptions ? 1 : 0);
        Grid.SetColumn(CutoffToggle, wrapOptions ? 1 : 2);
        Grid.SetRow(ClearSelectionButton, wrapOptions ? 2 : wrapReset ? 1 : 0);
        Grid.SetColumn(ClearSelectionButton, wrapOptions ? 0 : wrapReset ? 2 : 4);
        Grid.SetColumnSpan(ClearSelectionButton, wrapOptions ? 2 : 1);
        InspectionControls.RowSpacing = wrapReset ? 4 : 0;
        Overview.Width = Math.Min(180, Math.Max(0, available - controlsWidth - 16));
        Overview.Height = Math.Min(NavigationContent.ActualHeight, Overview.Width / 2.4);
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
        bool hidingSpectrum = Spek.ShowSpectrum && ProfileToggle.IsChecked != true;
        Spek.ShowSpectrum = ProfileToggle.IsChecked == true;
        Spek.ShowCutoffMarker = CutoffToggle.IsChecked == true;
        if (hidingSpectrum) Spek.ClearSelection();
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
