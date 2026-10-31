using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Core.Sources;
using FluentDL.Contracts.Services;
using FluentDL.Core.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Windows.Storage.Streams;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.ViewModels;

public class QueueObject : SongSearchObject, INotifyPropertyChanged
{
    private QueueArtworkState artworkState;
    [JsonIgnore]
    public QueueArtworkState ArtworkState
    {
        get => artworkState;
        set
        {
            if (artworkState == value) return;
            artworkState = value;
            OnPropertyChanged();
        }
    }
    [JsonIgnore]
    private string? _resultString;
    
    [JsonIgnore]
    public string? ResultString
    {
        get => _resultString;
        set
        {
            if (_resultString != value)
            {
                _resultString = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    private bool _isRunning;
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    // Created on first read, on the UI thread, so a row can also be built off it (tests, background restores).
    [JsonIgnore]
    private SolidColorBrush? _convertBadgeColor;

    [JsonIgnore]
    public SolidColorBrush ConvertBadgeColor
    {
        get => _convertBadgeColor ??= new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        set
        {
            if (_convertBadgeColor != value)
            {
                _convertBadgeColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConvertBadgeVisibility));
            }
        }
    }

    [JsonIgnore]
    public Visibility ConvertBadgeVisibility => _convertBadgeColor is { Color.A: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Set for an album row: its track count. Null for a track. Saved with the queue.</summary>
    public int? AlbumTracksCount { get; set; }

    /// <summary>
    /// An album row's tracks as its source listed them. Saved with the queue: Spotify and YouTube albums can't list
    /// their tracks again from the id alone, and Spotify's preview reads its cover from AdditionalFields.
    /// </summary>
    public List<SongSearchObject>? TrackList { get; set; }

    [JsonIgnore]
    public bool IsAlbum => AlbumTracksCount != null;

    public static bool IsAlbumItem(object? item) => item is AlbumSearchObject || item is QueueObject { IsAlbum: true };

    /// <summary>The object for source calls: an AlbumSearchObject for an album row, the row itself for a track.</summary>
    public static SongSearchObject Unwrap(SongSearchObject song) =>
        song is QueueObject { IsAlbum: true } row ? ToAlbum(row, row.AlbumTracksCount!.Value) : song;

    /// <summary>Restores a saved row: an album when it was saved with a track count, else a track.</summary>
    internal static SongSearchObject? ParseSaved(string json)
    {
        SongSearchObject? song;
        Newtonsoft.Json.Linq.JObject saved;
        try
        {
            song = Newtonsoft.Json.JsonConvert.DeserializeObject<SongSearchObject>(json);
            saved = Newtonsoft.Json.Linq.JObject.Parse(json);
        }
        catch (Newtonsoft.Json.JsonException e)
        {
            Serilog.Log.Warning(e, "Skipped an unreadable saved queue row");
            return null;
        }
        if (song == null) return null;
        if (saved["AlbumTracksCount"] is { Type: Newtonsoft.Json.Linq.JTokenType.Integer } count)
        {
            // Read it as an album too, so its TrackList and AdditionalFields come back.
            var album = saved.ToObject<AlbumSearchObject>() ?? ToAlbum(song, (int)count);
            album.TracksCount = (int)count;
            song = album;
        }
        return song;
    }

    public static AlbumSearchObject ToAlbum(SongSearchObject song, int tracksCount) => new()
    {
        Title = song.Title, ImageLocation = song.ImageLocation, Id = song.Id, ReleaseDate = song.ReleaseDate,
        Artists = song.Artists, Duration = song.Duration, Rank = song.Rank, AlbumName = song.AlbumName, Source = song.Source,
        Explicit = song.Explicit, TrackPosition = song.TrackPosition, Isrc = song.Isrc, QueueCounter = song.QueueCounter,
        AudioFormat = song.AudioFormat, AdditionalFields = song.AdditionalFields, TracksCount = tracksCount,
        TrackList = (song as QueueObject)?.TrackList?.ToList() ?? (song as AlbumSearchObject)?.TrackList?.ToList(),
        LocalBitmapImage = song.LocalBitmapImage,
    };

    public QueueObject(SongSearchObject song)
    {
        Title = song.Title;
        ImageLocation = song.ImageLocation;
        Id = song.Id;
        ReleaseDate = song.ReleaseDate;
        Artists = song.Artists;
        Duration = song.Duration;
        Rank = song.Rank;
        AlbumName = song.AlbumName;
        Source = song.Source;
        Explicit = song.Explicit;
        TrackPosition = song.TrackPosition;
        ResultString = null;
        IsRunning = false;
        LocalBitmapImage = song.LocalBitmapImage;
        ArtworkState = song.LocalBitmapImage != null ? QueueArtworkState.Ready : QueueArtworkState.Unavailable;
        Isrc = song.Isrc;
        QueueCounter = song.QueueCounter;
        AudioFormat = song.AudioFormat;
        // Sources keep data here that they need later, such as Deezer's preview address and Spotify's album cover.
        AdditionalFields = song.AdditionalFields == null ? null : new Dictionary<string, object?>(song.AdditionalFields);
        TrackList = (song as AlbumSearchObject)?.TrackList?.ToList() ?? (song as QueueObject)?.TrackList?.ToList();
        AlbumTracksCount = song switch
        {
            AlbumSearchObject album => album.TracksCount,
            QueueObject row => row.AlbumTracksCount,
            _ => null,
        };
    }
}

public partial class QueueViewModel : ObservableRecipient
{
    private static readonly HttpClient _httpClient = new() { MaxResponseContentBufferSize = ArtworkThumbnail.MaximumEncodedBytes };
    private static readonly BitmapImage UnloadedPlaceholder = new(new Uri("ms-appx:///Assets/Unloaded.jpg"));
    internal static BitmapImage ArtworkPlaceholder => UnloadedPlaceholder;
    public static ObservableCollection<QueueObject> Source
    {
        get;
        set;
    } = new ObservableCollection<QueueObject>();

    private int _successCount;

    public int SuccessCount
    {
        get => _successCount;
        set {
            if (_successCount != value)
            {
                _successCount = value;
                OnPropertyChanged();
            }
        }
    }

    private int _warningCount;

    public int WarningCount
    {
        get => _warningCount;
        set
        {
            if (_warningCount != value)
            {
                _warningCount = value;
                OnPropertyChanged();
            }
        }
    }

    private int _errorCount;

    public int ErrorCount
    {
        get => _errorCount;
        set
        {
            if (_errorCount != value)
            {
                _errorCount = value;
                OnPropertyChanged();
            }
        }
    }

    private static ILocalSettingsService localSettings = App.GetService<ILocalSettingsService>();

    public static DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private static HashSet<string> trackSet = new HashSet<string>();
    private static QueueArtworkLoader? artwork;
    private static bool artworkNetworkStarted;
    internal static int ArtworkEntryCount => artwork?.EntryCount ?? 0;
    private static QueueArtworkLoader Artwork => artwork ??= CreateArtworkLoader();
    private static QueueArtworkLoader CreateArtworkLoader()
    {
        var loader = new QueueArtworkLoader(Source, dispatcher, UnloadedPlaceholder);
        loader.ArtworkChanged += url => ArtworkChanged?.Invoke(url);
        if (artworkNetworkStarted) loader.StartNetwork();
        return loader;
    }

    internal static void StartArtworkNetwork()
    {
        artworkNetworkStarted = true;
        artwork?.StartNetwork();
    }

    public const int BulkAddThreshold = 100;
    public static bool IsBulkAdding { get; private set; }
    public static string BulkAddText { get; private set; } = string.Empty;
    public static double BulkAddProgress { get; private set; }
    public static int BulkArtworkFailures { get; private set; }
    public static InfoBarSeverity BulkAddSeverity { get; private set; } = InfoBarSeverity.Success;
    public static event Action? BulkAddProgressChanged;
    private static CancellationTokenSource? bulkCancellation;
    public static bool IsClosing { get; private set; }
    private static Task? shutdown;

    internal static Task ShutdownAsync()
    {
        if (shutdown != null) return shutdown;
        IsClosing = true;
        bulkCancellation?.Cancel();
        return shutdown = FinishShutdownAsync(ApplicationActivity.StopFileWritesAsync());
    }

    internal static event Action<string>? ArtworkChanged;
    internal static bool TryGetArtwork(string url, out BitmapImage? image, out QueueArtworkState state)
    {
        image = null;
        state = QueueArtworkState.Unavailable;
        return artwork != null && artwork.TryGet(url, out image, out state);
    }

    private static async Task FinishShutdownAsync(Task fileWrites)
    {
        try { await fileWrites; }
        catch (Exception error) { Serilog.Log.Warning(error, "A file writer failed while the app was closing"); }
        if (artwork != null) await artwork.StopAsync();
        await DatabaseService.FlushOnCloseAsync();
    }

    public static async Task<int> AddManyAsync(IReadOnlyList<SongSearchObject> songs, Action<string>? progress = null)
    {
        if (IsClosing) return 0;
        if (IsBulkAdding) throw new InvalidOperationException("Wait for the current queue addition to finish.");
        using var activity = ApplicationActivity.Gate.EnterWork();
        var added = 0;
        var large = songs.Count >= BulkAddThreshold;
        if (!large)
        {
            foreach (var song in songs) if (Add(song) != null) added++;
            return added;
        }
        using var cancellation = new CancellationTokenSource();
        bulkCancellation = cancellation;
        IsBulkAdding = true;
        BulkArtworkFailures = 0;
        BulkAddSeverity = InfoBarSeverity.Success;
        void Report(string text, double fraction)
        {
            if (IsClosing) return;
            BulkAddText = text;
            BulkAddProgress = fraction * 100;
            progress?.Invoke(text);
            if (!IsClosing) BulkAddProgressChanged?.Invoke();
        }
        try
        {
            var covers = new HashSet<Task<bool>>();
            Report($"Adding to queue... 0 of {songs.Count:N0}", 0);
            await Task.Delay(1);
            for (var i = 0; i < songs.Count; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var row = Add(songs[i]);
                if (row != null)
                {
                    added++;
                    if (Artwork.CompletionFor(row) is { } cover) covers.Add(cover);
                }
                if ((i + 1) % 25 == 0 || i + 1 == songs.Count)
                {
                    Report($"Adding to queue... {i + 1:N0} of {songs.Count:N0}", (i + 1d) / songs.Count);
                    await Task.Delay(1);
                }
            }
            await DatabaseService.FlushAsync();
            var total = covers.Count;
            var completed = 0;
            Report($"Caching artwork... 0 of {total:N0} covers", 0);
            while (covers.Count > 0)
            {
                var done = await Task.WhenAny(covers).WaitAsync(cancellation.Token);
                covers.Remove(done);
                if (!await done) BulkArtworkFailures++;
                completed++;
                Report($"Caching artwork... {completed:N0} of {total:N0} covers", completed / (double)total);
            }
            await DatabaseService.FlushAsync();
            var duplicates = songs.Count - added;
            if (BulkArtworkFailures > 0) BulkAddSeverity = InfoBarSeverity.Warning;
            BulkAddText = $"Added {added:N0} items to queue"
                + (duplicates > 0 ? $" ({duplicates:N0} duplicates ignored)" : "")
                + (BulkArtworkFailures > 0 ? $". {BulkArtworkFailures:N0} {(BulkArtworkFailures == 1 ? "cover" : "covers")} could not be cached and will retry after restart." : ". Artwork cached.");
            return added;
        }
        catch (OperationCanceledException)
        {
            BulkAddSeverity = InfoBarSeverity.Informational;
            BulkAddText = "Adding to queue cancelled.";
            return added;
        }
        catch
        {
            BulkAddSeverity = InfoBarSeverity.Error;
            BulkAddText = "Could not finish adding to queue. Some items may have been added.";
            throw;
        }
        finally
        {
            IsBulkAdding = false;
            bulkCancellation = null;
            if (!IsClosing) BulkAddProgressChanged?.Invoke();
        }
    }


    public QueueDisplaySettings QueueDisplay { get; }

    public QueueViewModel(QueueDisplaySettings queueDisplay)
    {
        QueueDisplay = queueDisplay;
        localSettings = App.GetService<ILocalSettingsService>();
        dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public static async Task UpdateShortcutVisibility() {
        var shortcuts = QueueShortcuts.Shared;
        shortcuts.ShareVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueShareChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        shortcuts.DownloadCoverVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueDownloadCoverChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        shortcuts.RemoveVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueRemoveChecked) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public static async Task<Stream?> GetStreamFromUrl(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        try
        {
            return await ArtworkThumbnail.DownloadAsync(_httpClient, uri);
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Queue image request failed");
            return null;
        }
    }

    public static async Task<IRandomAccessStream?> GetRandomAccessStreamOptimized(string? uri)
    {
        using var stream = await GetStreamFromUrl(uri);
        if (stream == null) return null;

        var jpeg = await ArtworkThumbnail.CreateJpegAsync(stream, 76);
        var ras = new InMemoryRandomAccessStream();
        try
        {
            await ras.WriteAsync(jpeg.AsBuffer());
            ras.Seek(0);
            return ras;
        }
        catch { ras.Dispose(); throw; }
    }

    public static int GetNextOrderCounter()
    {
        if (Source.Count == 0) return 0;
        return (Source.Last().QueueCounter ?? 0) + 1;
    }

    public static Task<QueueObject?> CreateQueueObject(SongSearchObject song, int? queueCounter)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        var queueObj = new QueueObject(song) { QueueCounter = queueCounter };
        // Persist only after collision resolution chooses the row and position to retain.
        return Task.FromResult<QueueObject?>(queueObj);
    }

    public static QueueObject? Add(SongSearchObject? song) => Admit(song, persist: true);

    private static QueueObject? Admit(SongSearchObject? song, bool persist)
    {
        if (IsClosing || ApplicationActivity.Gate.IsInstalling) return null;
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (song == null || trackSet.Contains(GetHash(song)))
        {
            return null;
        }

        var hash = GetHash(song);
        var queueObj = new QueueObject(song);
        queueObj.QueueCounter ??= GetNextOrderCounter();  // Only set order if new addition
        _ = Artwork;
        if (persist) _ = SaveMetadataAsync(queueObj);
        Source.Add(queueObj);
        trackSet.Add(hash);

        return queueObj;
    }

    private static async Task SaveMetadataAsync(QueueObject row)
    {
        try { await DatabaseService.SaveMetadataAsync(GetHash(row), row.ToString()); }
        catch (Exception error) { Serilog.Log.Error(error, "Queue metadata could not be saved"); }
    }

    public static async Task Remove(SongSearchObject song)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        var hash = GetHash(song);
        trackSet.Remove(hash);
        Source.Remove(Source.First(x => GetHash(x) == hash));
        await DatabaseService.Remove(hash);
    }

    public static async Task Replace(int index, QueueObject? queueObj)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (queueObj == null || IsClosing) return;
        _ = Artwork;
        var result = QueueTrackReplacement.Apply(Source, trackSet, index, queueObj);
        if (result == null) return;
        await DatabaseService.ReplaceTrackAsync(result.RemovedHash, GetHash(result.Row), result.Row.ToString());
    }

    public static async Task Clear()
    {
        bulkCancellation?.Cancel();
        using var activity = ApplicationActivity.Gate.EnterWork();
        Source.Clear();
        trackSet.Clear();
        await DatabaseService.Clear();
    }

    public static async Task ClearSources(HashSet<string> sources)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (sources == null) throw new ArgumentNullException(nameof(sources));
        if (sources.Count == 0) return;

        // Every source selected: clear everything at once.
        if (sources.Count >= SourceCatalog.All.Count)
        {
            await Clear();
            return;
        }

        var itemsToRemove = Source
            .Where(item => sources.Contains(item.Source))
            .ToList();

        foreach (var item in itemsToRemove)
        {
            await Remove(item);
        }
    }

    public static void Reset()
    {
        foreach (QueueObject queueObject in Source)
        {
            queueObject.ResultString = null;
        }
    }

    public static void ResetConversionResults()
    {
        // Reset the badge color
        foreach (QueueObject queueObject in Source)
        {
            queueObject.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }

    internal static string GetHash(SongSearchObject song)
    {
        return QueueTrackReplacement.KeyOf(song);
    }

    internal static Task SaveForUpdateAsync(CancellationToken token) => DatabaseService.SaveForUpdateAsync(
        Source.Select(row => (GetHash(row), row.ToString())).ToArray(), token);

    internal static async Task<QueueAlbumExpansionPlan?> ExpandAlbumAsync(QueueObject album, IReadOnlyList<SongSearchObject> tracks)
    {
        using var exclusive = ApplicationActivity.Gate.TryEnterExclusiveWork()
            ?? throw new InvalidOperationException("Finish active downloads, conversions or playlist uploads before expanding an album.");
        var index = Source.IndexOf(album);
        if (index < 0) return null;
        var plan = QueueAlbumExpansion.Plan(album, tracks, Source);
        // Save the current queue first, including any row whose cover could not be fetched.
        // The exclusive gate prevents new work until storage and the collection agree.
        await SaveForUpdateAsync(CancellationToken.None);
        await ReplaceAlbumRowsAsync(album, plan.Rows);
        return plan;
    }

    /// <summary>Replaces <paramref name="album"/> with the rows it downloaded into, in album order.</summary>
    public static async Task ReplaceWithMany(QueueObject album, IReadOnlyList<QueueObject> rows)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        await ReplaceAlbumRowsAsync(album, rows);
    }

    // Each track gets its own position, starting at the album's. Later rows move down to make room.
    private static async Task ReplaceAlbumRowsAsync(QueueObject album, IReadOnlyList<QueueObject> rows)
    {
        var index = Source.IndexOf(album);
        if (index < 0) return;
        var position = album.QueueCounter ?? index;
        var hashes = new HashSet<string>(trackSet, StringComparer.Ordinal);
        var accepted = rows.Where(row => hashes.Add(GetHash(row))).ToArray();
        for (var i = 0; i < accepted.Length; i++) accepted[i].QueueCounter = position + i;
        var shift = Math.Max(0, accepted.Length - 1);
        // Queue the storage transaction, then make the same change before yielding. Additions and conversions that run
        // during the write then use the new positions, and their writes follow this one.
        var write = DatabaseService.ReplaceAlbumAsync(GetHash(album), position, shift, accepted.Select(row => (GetHash(row), row.ToString())).ToArray());
        trackSet.Remove(GetHash(album));
        Source.RemoveAt(index);
        if (shift > 0)
            foreach (var row in Source)
                if (row.QueueCounter > position) row.QueueCounter += shift;
        foreach (var row in accepted)
        {
            trackSet.Add(GetHash(row));
            Source.Insert(index++, row);
        }
        await write;
    }

    public static async Task LoadSaveQueue() 
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        try
        {
            Source.Clear();
            trackSet.Clear();
            var items = await DatabaseService.LoadQueueJSON();
            var sortedList = new List<(string Hash, SongSearchObject Song)>();

            foreach (var item in items)
            {
                var saved = await Task.Run(() => QueueObject.ParseSaved(item.Value));
                if (saved == null) continue;
                sortedList.Add((item.Key, saved));
            }

            foreach (var pair in sortedList.OrderBy(pair => pair.Song.QueueCounter).ThenBy(pair => pair.Hash, StringComparer.Ordinal))
            {
                SongSearchObject song = pair.Song;

                // Keep the saved metadata and order even when a local file is temporarily unavailable.
                song.LocalBitmapImage = UnloadedPlaceholder;
                var queueObj = Admit(song, persist: false);
                if (song.Source == SourceIds.Local)
                {
                    if (queueObj != null && LocalExplorerViewModel.ParseFile(song.Id) != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var image = await LocalExplorerViewModel.GetBitmapImageBackground(song.Id, dispatcher);
                            if (image != null)
                            {
                                dispatcher.TryEnqueue(() =>
                                {
                                    queueObj.LocalBitmapImage = image;
                                });
                            }
                        });
                    }
                }
            }
            Serilog.Log.Information("Queue restored with {ItemCount} items", Source.Count);
        } catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Queue restore failed; some items may be missing");
        }
    }
}
