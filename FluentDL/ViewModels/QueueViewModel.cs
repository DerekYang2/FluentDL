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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Windows.Storage.Streams;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.ViewModels;

public class QueueObject : SongSearchObject, INotifyPropertyChanged
{
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

    /// <summary>Orders the tracks an album expanded into, which share the album's QueueCounter. Saved with the queue.</summary>
    public int? QueueSubOrder { get; set; }

    [JsonIgnore]
    public bool IsAlbum => AlbumTracksCount != null;

    public static bool IsAlbumItem(object? item) => item is AlbumSearchObject || item is QueueObject { IsAlbum: true };

    /// <summary>The object for source calls: an AlbumSearchObject for an album row, the row itself for a track.</summary>
    public static SongSearchObject Unwrap(SongSearchObject song) =>
        song is QueueObject { IsAlbum: true } row ? ToAlbum(row, row.AlbumTracksCount!.Value) : song;

    /// <summary>Restores a saved row: an album when it was saved with a track count, else a track.</summary>
    internal static (SongSearchObject Song, int SubOrder)? ParseSaved(string json)
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
        var subOrder = saved["QueueSubOrder"] is { Type: Newtonsoft.Json.Linq.JTokenType.Integer } sub ? (int)sub : 0;
        return (song, subOrder);
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
        if (song is QueueObject other) QueueSubOrder = other.QueueSubOrder;
    }
}

public partial class QueueViewModel : ObservableRecipient
{
    private static readonly HttpClient _httpClient = new();
    private static readonly BitmapImage UnloadedPlaceholder = new(new Uri("ms-appx:///Assets/Unloaded.jpg"));
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
            HttpClient httpClient = new();
            var response = await httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
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

        using var image = await Image.LoadAsync(stream);
        await Task.Run(()=>image.Mutate(ctx => ctx.Resize(new ResizeOptions { 
            Size = new Size(76, 76), 
            Mode = ResizeMode.Min, 
            Sampler = KnownResamplers.Lanczos3 
        })));
        var ras = new InMemoryRandomAccessStream();
        var outStream = ras.AsStreamForWrite();
        await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = 80 });
        await outStream.FlushAsync();

        ras.Seek(0);
        return ras;
    }

    public static int GetNextOrderCounter()
    {
        if (Source.Count == 0) return 0;
        return (Source.Last().QueueCounter ?? 0) + 1;
    }

    public static async Task<QueueObject?> CreateQueueObject(SongSearchObject song, int? queueCounter, int? subOrder = null)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        var queueObj = new QueueObject(song)
        {
            QueueCounter = queueCounter,
            QueueSubOrder = subOrder,
        };
        if (queueObj.LocalBitmapImage == null) // Create a local bitmap image for queue objects to prevent disappearing listview images
        {
            using var memoryStream = await GetRandomAccessStreamOptimized(queueObj.ImageLocation); // Get the memory stream from the url
            if (memoryStream == null) return null;

            var bitmapImage = new BitmapImage { DecodePixelHeight = 76 }; // Create a new bitmap image
            await bitmapImage.SetSourceAsync(memoryStream);
            queueObj.LocalBitmapImage = bitmapImage; // Set the local bitmap image

            await DatabaseService.QueueSave(GetHash(queueObj), queueObj.ToString(), memoryStream);
        } else
        {
            await DatabaseService.QueueSave(GetHash(queueObj), queueObj.ToString(), null);
        }

        return queueObj;
    }

    public static QueueObject? Add(SongSearchObject? song)
    {
        if (ApplicationActivity.Gate.IsInstalling) return null;
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (song == null || trackSet.Contains(GetHash(song)))
        {
            return null;
        }

        var hash = GetHash(song);
        var queueObj = new QueueObject(song);
        queueObj.QueueCounter ??= GetNextOrderCounter();  // Only set order if new addition
        Source.Add(queueObj);
        trackSet.Add(hash);

        if (queueObj.LocalBitmapImage == null)  // Create a bitmapimage to prevent disappearing listview images
        {
            _ = ApplicationActivity.RunBackground(async () =>
            {
                try
                {
                    // Background
                    var memoryStream = await GetRandomAccessStreamOptimized(queueObj.ImageLocation);
                    if (memoryStream == null) return;

                    // Save the bitmap
                    await DatabaseService.QueueSave(hash, queueObj.ToString(), memoryStream);

                    // UI Thread
                    dispatcher.TryEnqueue(async () =>
                    {
                        var bitmapImage = new BitmapImage { DecodePixelHeight = 76 };
                        await bitmapImage.SetSourceAsync(memoryStream);
                        queueObj.LocalBitmapImage = bitmapImage;
                        memoryStream.Dispose();
                    });
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Background queue image load failed");
                }
            });
        } else if (queueObj.Source == SourceIds.Local)
        {
            _ = DatabaseService.QueueSave(hash, queueObj.ToString(), null);
        }

        return queueObj;
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
        if (queueObj == null || index < 0) return; // a negative index: the row was removed meanwhile

        var oldHash = GetHash(Source[index]);
        trackSet.Remove(oldHash); // Remove old from trackset
        Source[index] = queueObj; // Set to new object
        trackSet.Add(GetHash(queueObj)); // Add new hash to trackset
        await DatabaseService.Remove(oldHash);
    }

    public static async Task Clear()
    {
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

    private static string GetHash(SongSearchObject song)
    {
        // Albums get their own key, so an album and a track with the same id can both be queued.
        return (QueueObject.IsAlbumItem(song) ? "album:" : "") + song.Source + song.Id;
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
        await DatabaseService.ReplaceAlbumAsync(GetHash(album), plan.Rows.Select(row => (GetHash(row), row.ToString())).ToArray());
        trackSet.Remove(GetHash(album));
        Source.RemoveAt(index);
        foreach (var row in plan.Rows)
        {
            trackSet.Add(GetHash(row));
            Source.Insert(index++, row);
        }
        return plan;
    }

    /// <summary>
    /// Replaces the album row at <paramref name="index"/> with the rows it expanded into, in order. They keep the album's
    /// position through restarts.
    /// </summary>
    public static async Task ReplaceWithMany(int index, IReadOnlyList<QueueObject> rows)
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        if (index < 0) return; // the album row was removed meanwhile
        var album = Source[index];
        trackSet.Remove(GetHash(album));
        Source.RemoveAt(index);
        await DatabaseService.Remove(GetHash(album));
        var position = index;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // CreateQueueObject already saved the row with its image and order.
            if (!trackSet.Add(GetHash(row))) continue; // already in the queue elsewhere
            Source.Insert(position++, row);
        }
    }

    public static async Task LoadSaveQueue() 
    {
        using var activity = ApplicationActivity.Gate.EnterWork();
        try
        {
            Source.Clear();
            trackSet.Clear();
            var items = await DatabaseService.LoadQueueJSON();
            var sortedList = new List<(string, SongSearchObject, int)>();

            foreach (var item in items)
            {
                var saved = await Task.Run(() => QueueObject.ParseSaved(item.Value));
                if (saved == null) continue;
                sortedList.Add((item.Key, saved.Value.Song, saved.Value.SubOrder));
            }

            foreach (var pair in sortedList.OrderBy(pair => pair.Item2.QueueCounter).ThenBy(pair => pair.Item3))
            {
                string hash = pair.Item1;
                SongSearchObject song = pair.Item2;

                // If local, set the local bitmap image
                if (song.Source == SourceIds.Local)
                {
                    var localSong = LocalExplorerViewModel.ParseFile(song.Id);  // Re-parse the file
                    if (localSong != null)
                    {
                        localSong.LocalBitmapImage = UnloadedPlaceholder;
                        var queueObj = Add(localSong);
                        // Get the bitmap image from the file
                        if (queueObj != null)
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
                else
                {
                    song.LocalBitmapImage = UnloadedPlaceholder;
                    var queueObj = Add(song);
                    // Get the bitmap image from the queue
                    if (queueObj != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var image = await DatabaseService.GetBitmapAsync(hash, dispatcher);
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
