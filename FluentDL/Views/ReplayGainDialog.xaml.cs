using System.Collections.ObjectModel;
using FluentDL.Core.ReplayGain;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FluentDL.Views;

// What Write tags would do to a row's file. Rows get a status once the analysis is complete, because album values
// can change it until then.
public enum WriteStatus { None, New, Update, UpToDate }

// One row in the ReplayGain dialog, with values already formatted for display. Order is the track's place in
// Local Explorer, which keeps rows sorted as they arrive. On an Update row, the Current values are the file's
// existing gains that writing would change, or "none" for a gain the file doesn't have. It's a class with read-only
// properties because the XAML compiler can't generate code for a record's init-only properties.
public sealed class ReplayGainRow(
    SongSearchObject song,
    int order,
    string loudness,
    string trackGain,
    string albumGain,
    string? warning,
    string? currentTrackGain,
    string? currentAlbumGain,
    WriteStatus status = WriteStatus.None,
    string? statusTip = null)
{
    public const string UpToDateTip = "This file already has these values, so Write tags leaves it unchanged.";

    public SongSearchObject Song { get; } = song;
    public int Order { get; } = order;
    public string Loudness { get; } = loudness;
    public string TrackGain { get; } = trackGain;
    public string AlbumGain { get; } = albumGain;
    public string? Warning { get; } = warning;
    public string? CurrentTrackGain { get; } = currentTrackGain;
    public string? CurrentAlbumGain { get; } = currentAlbumGain;
    public WriteStatus Status { get; } = status;
    public string? StatusTip { get; } = statusTip;
    public Visibility NewVisibility => Status == WriteStatus.New ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpdateVisibility => Status == WriteStatus.Update ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpToDateVisibility => Status == WriteStatus.UpToDate ? Visibility.Visible : Visibility.Collapsed;

    // After a successful write the file has the new gains, so the row is up to date.
    public ReplayGainRow Written() => new(Song, Order, Loudness, TrackGain, AlbumGain, Warning, null, null, WriteStatus.UpToDate, UpToDateTip);

    public ReplayGainRow WithNote(string note) =>
        new(Song, Order, Loudness, TrackGain, AlbumGain, Warning is null ? note : $"{note}\n{Warning}", CurrentTrackGain, CurrentAlbumGain, Status, StatusTip);
}

// Measures the tracks in Local Explorer and writes their ReplayGain tags. Analyze fills in the results, Write tags
// saves them, and changing an option discards them so stale gains can't be written.
public sealed partial class ReplayGainDialog : UserControl
{
    private const string WikiLink = "https://github.com/DerekYang2/FluentDL/wiki/ReplayGain";

    private enum State { Ready, Analyzing, Analyzed, Writing }

    private State state = State.Ready;
    private List<SongSearchObject> songs = [];
    // Each song's place in songs, by path.
    private Dictionary<string, int> order = new(StringComparer.OrdinalIgnoreCase);
    private Action releasePreviewFile = () => { };
    private CancellationTokenSource? cancellation;
    // The scan waiting for Write tags, and the options it was calculated with. Set only in the Analyzed state.
    private ReplayGainScan? scan;
    private ReplayGainOptions? scanOptions;

    public ObservableCollection<ReplayGainRow> Rows { get; } = new();

    public ReplayGainDialog()
    {
        InitializeComponent();
        ResultsList.ItemsSource = Rows;
        Rows.CollectionChanged += (_, _) => NoResultsText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Opens the dialog for Local Explorer's tracks. releasePreviewFile stops the preview player, which keeps its
    // file open and would block saving it. Reopening during an analysis or a write shows it as it is.
    public async Task ShowAsync(IReadOnlyList<SongSearchObject> tracks, XamlRoot xamlRoot, Action releasePreviewFile)
    {
        Dialog.XamlRoot = xamlRoot;
        if (state is State.Ready or State.Analyzed)
        {
            // The track list may have changed since the last visit, so start again from the saved settings.
            songs = tracks.DistinctBy(song => song.Id, StringComparer.OrdinalIgnoreCase).ToList();
            order = songs.Select((song, index) => (song.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            this.releasePreviewFile = releasePreviewFile;
            ClearResults();
            OffsetSlider.Value = await SettingsViewModel.GetSetting<double?>(SettingsViewModel.ReplayGainOffset) ?? 0;
            ClipCheckBox.IsChecked = await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.ReplayGainPreventClipping) ?? false;
            SetInfo(InfoBarSeverity.Informational,
                $"Measure the loudness of the <b>{songs.Count}</b> tracks in Local Explorer. <a href='{WikiLink}'>Read more on the wiki</a>.");
        }

        await Dialog.ShowAsync();
    }

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (state == State.Analyzed)
        {
            await WriteAsync();
        }
        else
        {
            await AnalyzeAsync();
        }
    }

    private void Dialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        cancellation?.Cancel();
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Analyze, Write tags and Cancel keep the dialog open. Close and Escape close it and stop work in progress.
        if (args.Result != ContentDialogResult.None)
        {
            args.Cancel = true;
            return;
        }

        cancellation?.Cancel();
    }

    private async Task AnalyzeAsync()
    {
        var options = ReadOptions();
        if (songs.Count == 0)
        {
            SetInfo(InfoBarSeverity.Warning, "There are no tracks in Local Explorer to analyze.");
            return;
        }

        if (!options.TrackGain && !options.AlbumGain)
        {
            SetInfo(InfoBarSeverity.Warning, "Choose track gain, album gain, or both.");
            return;
        }

        ClearResults();
        var token = StartOperation();
        SetState(State.Analyzing);
        // Album gain and skipping read every file's tags before measuring starts.
        SetInfo(InfoBarSeverity.Informational, options.AlbumGain || options.SkipTaggedTracks
            ? "Reading tags ..."
            : $"Analyzing <b>0 of {songs.Count}</b> tracks ...");

        // A row is added when its track's result arrives. Progress reports arrive on the UI thread and can land
        // after the scan returns, so later ones are ignored.
        var finished = false;
        var progress = new Progress<ReplayGainProgress>(update =>
        {
            if (finished || update.Total == 0) return;
            ShowProgress("Analyzing", update.Completed, update.Total);
            if (update.Track is { } track)
            {
                SetRow(track.Path, (song, index) => CreateRow(song, index, track, options, albumDone: false));
            }
            else if (update.Failure is { } failure)
            {
                SetRow(failure.Path, (song, index) => FailedRow(song, index, failure));
            }
        });

        ReplayGainScan result;
        try
        {
            result = await ReplayGainScanner.ScanAsync(songs.Select(song => new ReplayGainTrack(song.Id)).ToList(), options, progress, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            SetState(State.Ready);
            SetInfo(InfoBarSeverity.Informational, "Analysis cancelled. No tags were written.");
            return;
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "ReplayGain analysis failed");
            SetState(State.Ready);
            SetInfo(InfoBarSeverity.Error, $"Analysis failed: {e.Message}");
            return;
        }
        finally
        {
            finished = true;
        }

        // Album gains only exist now that every track is measured. Rows change in place to keep the scroll position.
        foreach (var track in result.Results)
        {
            SetRow(track.Path, (song, index) => CreateRow(song, index, track, options, albumDone: true));
        }

        foreach (var failure in result.Failures)
        {
            SetRow(failure.Path, (song, index) => FailedRow(song, index, failure));
        }

        var toWrite = result.Results.Count(track => !ReplayGainScanner.HasTags(track, options));
        SetInfo(SummarySeverity(result, options), Summary(result, options, toWrite));
        if (toWrite > 0)
        {
            scan = result;
            scanOptions = options;
            SetState(State.Analyzed);
        }
        else
        {
            SetState(State.Ready);
        }
    }

    private string Summary(ReplayGainScan result, ReplayGainOptions options, int toWrite)
    {
        var lowered = result.Results.Count(track => (options.TrackGain && track.TrackAdjusted) || (options.AlbumGain && track.AlbumAdjusted));
        var clipping = result.Results.Count(track => (options.TrackGain && track.TrackClips) || (options.AlbumGain && track.AlbumClips));
        var withoutAlbumGain = options.AlbumGain ? result.Results.Count(track => track.AlbumGain is null) : 0;
        var unchanged = result.Results.Count - toWrite;
        var analyzedOf = songs.Count - result.Skipped.Count;

        var summary = analyzedOf == 0
            ? $"All <b>{songs.Count}</b> tracks already have ReplayGain tags, so none were analyzed."
            : $"Analyzed <b>{result.Results.Count} of {analyzedOf}</b> tracks.";
        if (analyzedOf > 0 && result.Skipped.Count > 0) summary += $" Skipped {result.Skipped.Count} that already have ReplayGain tags.";
        if (lowered > 0) summary += $" Lowered the gain of {lowered} to prevent clipping.";
        if (clipping > 0) summary += $" {clipping} would clip.";
        if (result.Failures.Count > 0) summary += $" {result.Failures.Count} could not be read.";
        if (withoutAlbumGain > 0) summary += $" {withoutAlbumGain} got no album gain, because a track on their album could not be read.";
        if (lowered + clipping + result.Failures.Count + withoutAlbumGain > 0) summary += " Hover over a warning icon for details.";
        if (unchanged > 0) summary += $" {unchanged} are up to date and won't be rewritten.";
        if (toWrite > 0) summary += " Select Write tags to save the gains.";
        else if (result.Results.Count > 0) summary += " There's nothing to write.";
        return summary;
    }

    private static InfoBarSeverity SummarySeverity(ReplayGainScan result, ReplayGainOptions options) =>
        result.Failures.Count > 0 || result.Results.Any(track => (options.TrackGain && track.TrackClips) || (options.AlbumGain && track.AlbumClips))
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;

    private async Task WriteAsync()
    {
        var options = scanOptions!;
        // Files that already have these exact values aren't saved again.
        var toWrite = scan!.Results.Where(track => !ReplayGainScanner.HasTags(track, options)).ToList();
        var unchanged = scan.Results.Count - toWrite.Count;
        scan = null;
        scanOptions = null;
        var token = StartOperation();
        SetState(State.Writing);
        releasePreviewFile();

        var written = 0;
        var failed = 0;
        foreach (var track in toWrite)
        {
            if (token.IsCancellationRequested) break;
            ShowProgress("Writing tags to", written + failed, toWrite.Count);
            string? error = null;
            try
            {
                await Task.Run(() => ReplayGainScanner.WriteTags(track, options));
                written++;
            }
            catch (Exception e)
            {
                error = e.Message.Split('\n', 2)[0].Trim();
                failed++;
            }
            UpdateRowAfterWrite(track.Path, error);
        }

        SetState(State.Ready);
        var stopped = written + failed < toWrite.Count;
        var summary = stopped
            ? $"Stopped after writing ReplayGain tags to <b>{written} of {toWrite.Count}</b> tracks."
            : $"Wrote ReplayGain tags to <b>{written} of {toWrite.Count}</b> tracks.";
        if (unchanged > 0) summary += $" {unchanged} were already up to date.";
        if (failed > 0) summary += " Hover over a warning icon for details.";
        SetInfo(failed > 0 ? InfoBarSeverity.Warning : stopped ? InfoBarSeverity.Informational : InfoBarSeverity.Success, summary);
    }

    // Replaces the track's row, or inserts it at the track's place in Local Explorer's order if it isn't shown yet.
    private void SetRow(string path, Func<SongSearchObject, int, ReplayGainRow> create)
    {
        if (!order.TryGetValue(path, out var index)) return;
        var position = FindRow(index);
        if (position >= 0)
        {
            Rows[position] = create(songs[index], index);
        }
        else
        {
            Rows.Insert(~position, create(songs[index], index));
        }
    }

    // Binary search of Rows by Order. Like List.BinarySearch, a missing row returns the complement of where it goes.
    private int FindRow(int index)
    {
        int low = 0, high = Rows.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var current = Rows[middle].Order;
            if (current == index) return middle;
            if (current < index) low = middle + 1;
            else high = middle - 1;
        }
        return ~low;
    }

    // Written rows drop their Current line, since the file now has the new gain. Failed rows get a note.
    private void UpdateRowAfterWrite(string path, string? error)
    {
        if (!order.TryGetValue(path, out var index)) return;
        var position = FindRow(index);
        if (position < 0) return;
        var row = Rows[position];
        Rows[position] = error is null ? row.Written() : row.WithNote($"Could not write tags: {error}");
    }

    // albumDone says whether album values have been calculated. Before then, every row has track values only and no
    // status, since album values can still change what Write tags does.
    private static ReplayGainRow CreateRow(SongSearchObject song, int index, ReplayGainResult result, ReplayGainOptions options, bool albumDone)
    {
        var trackGain = options.TrackGain ? ReplayGainMath.FormatGain(result.TrackGain) : "";
        var albumGain = options.AlbumGain && result.AlbumGain is { } gain ? ReplayGainMath.FormatGain(gain) : "";
        // The same comparison WriteTags makes, so the status can't disagree with what gets written.
        var changes = albumDone ? ReplayGainScanner.Changes(result, options) : [];
        var status = !albumDone ? WriteStatus.None
            : changes.Count == 0 ? WriteStatus.UpToDate
            : result.Existing is null or { TrackGain: null, TrackPeak: null, AlbumGain: null, AlbumPeak: null } ? WriteStatus.New
            : WriteStatus.Update;
        // A new file's Current values would all be "none", which the New badge already says.
        var showCurrent = status == WriteStatus.Update;
        return new ReplayGainRow(
            song,
            index,
            double.IsNegativeInfinity(result.Loudness) ? "Silent" : $"{result.Loudness:0.0} LUFS",
            trackGain,
            albumGain,
            Notes(result, options, albumDone),
            showCurrent ? CurrentGain(changes, ReplayGainValue.TrackGain) : null,
            showCurrent ? CurrentGain(changes, ReplayGainValue.AlbumGain) : null,
            status,
            StatusTip(status, changes));
    }

    private static ReplayGainRow FailedRow(SongSearchObject song, int index, ReplayGainFailure failure) =>
        new(song, index, "", "", "", $"Could not analyze: {failure.Message}", null, null);

    // The file's existing value for a gain that writing would change, or "none" if the file doesn't have that gain.
    private static string? CurrentGain(List<ReplayGainChange> changes, ReplayGainValue gain) =>
        changes.FirstOrDefault(change => change.Value == gain) is { } change
            ? $"Current: {(change.Current is { } value ? ReplayGainMath.FormatGain(value) : "none")}"
            : null;

    private static string? StatusTip(WriteStatus status, List<ReplayGainChange> changes) => status switch
    {
        WriteStatus.New => "This file has no ReplayGain tags. Write tags adds them.",
        WriteStatus.Update => "Write tags will update this file:\n" + string.Join("\n", changes.Select(Describe)),
        WriteStatus.UpToDate => ReplayGainRow.UpToDateTip,
        _ => null,
    };

    // Such as "Changes track gain from +2.98 dB to +3.21 dB" or "Adds album peak 0.991100". Peaks are shown the
    // way tags store them, as linear values with six decimals.
    private static string Describe(ReplayGainChange change)
    {
        var (name, isGain) = change.Value switch
        {
            ReplayGainValue.TrackGain => ("track gain", true),
            ReplayGainValue.TrackPeak => ("track peak", false),
            ReplayGainValue.AlbumGain => ("album gain", true),
            _ => ("album peak", false),
        };
        string Show(double value) => isGain ? ReplayGainMath.FormatGain(value) : value.ToString("0.000000");
        return change.Current is { } current
            ? $"Changes {name} from {Show(current)} to {Show(change.New)}"
            : $"Adds {name} {Show(change.New)}";
    }

    private static string? Notes(ReplayGainResult result, ReplayGainOptions options, bool albumDone)
    {
        var notes = new List<string>();
        if (options.TrackGain)
        {
            AddGainNotes(notes, "track", result.TrackGain, result.TrackPeak, result.TrackClips, result.TrackAdjusted);
        }

        if (options.AlbumGain && result.AlbumGain is { } albumGain)
        {
            AddGainNotes(notes, "album", albumGain, result.AlbumPeak!.Value, result.AlbumClips, result.AlbumAdjusted);
        }
        else if (options.AlbumGain && albumDone)
        {
            notes.Add("This track has no album gain, because another track on its album could not be read.");
        }

        return notes.Count == 0 ? null : string.Join("\n", notes);
    }

    private static void AddGainNotes(List<string> notes, string kind, double gain, double peak, bool clips, bool adjusted)
    {
        if (adjusted)
        {
            notes.Add($"The {kind} gain was lowered to {ReplayGainMath.FormatGain(gain)} to prevent clipping.");
        }

        if (clips)
        {
            notes.Add($"With the {kind} gain applied, the peak reaches {gain + 20 * Math.Log10(peak):+0.00;-0.00} dBFS, which clips.");
        }
    }

    private ReplayGainOptions ReadOptions() => new(
        TrackGain: TrackCheckBox.IsChecked == true,
        AlbumGain: AlbumCheckBox.IsChecked == true,
        SingleAlbum: SingleAlbumCheckBox.IsChecked == true,
        Offset: OffsetSlider.Value,
        PreventClipping: ClipCheckBox.IsChecked == true,
        SkipTaggedTracks: SkipToggle.IsOn);

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        SingleAlbumCheckBox.IsEnabled = AlbumCheckBox.IsChecked == true;
        DiscardResults();
    }

    private void OffsetSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // The slider can raise this while the control is still being built.
        if (OffsetText == null) return;
        OffsetText.Text = $"Gain adjustment: {e.NewValue:+0.0;-0.0;0.0} dB";
        DiscardResults();
    }

    // Shown results were calculated with the old options, so they can't be written any more.
    private void DiscardResults()
    {
        if (Rows.Count == 0) return;
        ClearResults();
        SetInfo(InfoBarSeverity.Informational, "The options changed. Select Analyze to measure again.");
    }

    private void ClearResults()
    {
        Rows.Clear();
        scan = null;
        scanOptions = null;
        SetState(State.Ready);
    }

    private CancellationToken StartOperation()
    {
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        return cancellation.Token;
    }

    private void SetState(State next)
    {
        state = next;
        var busy = next is State.Analyzing or State.Writing;
        InfoProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InfoProgress.IsIndeterminate = true;
        Dialog.IsPrimaryButtonEnabled = !busy;
        Dialog.PrimaryButtonText = next == State.Analyzed ? "Write tags" : "Analyze";
        // Cancel stops an analysis or a write.
        Dialog.IsSecondaryButtonEnabled = busy;
        TrackCheckBox.IsEnabled = !busy;
        AlbumCheckBox.IsEnabled = !busy;
        SingleAlbumCheckBox.IsEnabled = !busy && AlbumCheckBox.IsChecked == true;
        ClipCheckBox.IsEnabled = !busy;
        OffsetSlider.IsEnabled = !busy;
        SkipToggle.IsEnabled = !busy;
    }

    private void ShowProgress(string action, int completed, int total)
    {
        InfoProgress.IsIndeterminate = false;
        InfoProgress.Value = (double)completed / total * 100;
        UrlParser.ParseTextBlock(InfoText, $"{action} <b>{completed} of {total}</b> tracks ...");
    }

    private void SetInfo(InfoBarSeverity severity, string text)
    {
        Info.Severity = severity;
        UrlParser.ParseTextBlock(InfoText, text);
    }
}
