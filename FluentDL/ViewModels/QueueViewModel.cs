using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;
using FluentDL.Core.Helpers;
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

    [JsonIgnore]
    private SolidColorBrush _convertBadgeColor = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    [JsonIgnore]
    public SolidColorBrush ConvertBadgeColor
    {
        get => _convertBadgeColor;
        set
        {
            if (_convertBadgeColor != value)
            {
                _convertBadgeColor = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    // Shortcut Visibilities
    public Visibility ShareVisibility
    {
        get;
        set;
    } = Visibility.Collapsed;

    [JsonIgnore]
    public Visibility DownloadCoverVisibility
    {
        get;
        set;
    } = Visibility.Collapsed;

    [JsonIgnore]
    public Visibility RemoveVisibility
    {
        get;
        set;
    } = Visibility.Collapsed;

    public QueueObject(SongSearchObject song, Visibility ShareVisibility, Visibility DownloadCoverVisibility, Visibility RemoveVisibility) 
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

        this.ShareVisibility = ShareVisibility;
        this.DownloadCoverVisibility = DownloadCoverVisibility;
        this.RemoveVisibility = RemoveVisibility;
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


    public static Visibility ShareVisibility = Visibility.Collapsed;
    public static Visibility DownloadCoverVisibility = Visibility.Collapsed;
    public static Visibility RemoveVisibility = Visibility.Collapsed;


    public QueueViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
        dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public static async Task UpdateShortcutVisibility() {
        ShareVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueShareChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        DownloadCoverVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueDownloadCoverChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        RemoveVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueRemoveChecked) == true ? Visibility.Visible : Visibility.Collapsed;
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
            Debug.WriteLine("ERROR CREATING STREAM: " + e.Message);
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

    public static async Task<QueueObject?> CreateQueueObject(SongSearchObject song, int? queueCounter)
    {
        var queueObj = new QueueObject(song, ShareVisibility, DownloadCoverVisibility, RemoveVisibility);
        queueObj.QueueCounter = queueCounter;
        if (queueObj.LocalBitmapImage == null) // Create a local bitmap image for queue objects to prevent disappearing listview images
        {
            using var memoryStream = await GetRandomAccessStreamOptimized(queueObj.ImageLocation); // Get the memory stream from the url
            if (memoryStream == null) return null;

            var bitmapImage = new BitmapImage { DecodePixelHeight = 76 }; // Create a new bitmap image
            await bitmapImage.SetSourceAsync(memoryStream);
            queueObj.LocalBitmapImage = bitmapImage; // Set the local bitmap image

            await DatabaseService.QueueSave(GetHash(queueObj), queueObj.ToString(), memoryStream);
        }

        return queueObj;
    }

    public static QueueObject? Add(SongSearchObject? song)
    {
        if (song == null || trackSet.Contains(GetHash(song)))
        {
            return null;
        }

        var hash = GetHash(song);
        var queueObj = new QueueObject(song, ShareVisibility, DownloadCoverVisibility, RemoveVisibility);
        queueObj.QueueCounter ??= GetNextOrderCounter();  // Only set order if new addition
        Source.Add(queueObj);
        trackSet.Add(hash);

        if (queueObj.LocalBitmapImage == null)  // Create a bitmapimage to prevent disappearing listview images
        {
            _ = Task.Run(async () =>
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
                    Debug.WriteLine($"Background image load failed: {ex.Message}");
                }
            });
        } else if (queueObj.Source == "local")
        {
            _ = DatabaseService.QueueSave(hash, queueObj.ToString(), null);
        }

        return queueObj;
    }

    public static async Task Remove(SongSearchObject song)
    {
        var hash = GetHash(song);
        trackSet.Remove(hash);
        Source.Remove(Source.First(x => GetHash(x) == hash));
        await DatabaseService.Remove(hash);
    }

    public static async Task Replace(int index, QueueObject? queueObj)
    {
        if (queueObj == null) return;

        var oldHash = GetHash(Source[index]);
        trackSet.Remove(oldHash); // Remove old from trackset
        Source[index] = queueObj; // Set to new object
        trackSet.Add(GetHash(queueObj)); // Add new hash to trackset
        await DatabaseService.Remove(oldHash);
    }

    public static async Task Clear()
    {
        Source.Clear();
        trackSet.Clear();
        await DatabaseService.Clear();
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
        return song.Source + song.Id;
    }

    public static async Task LoadSaveQueue() 
    {
        try
        {
            var items = await DatabaseService.LoadQueueJSON();
            var sortedList = new List<(string, SongSearchObject)>();

            foreach (var item in items)
            {
                var song = await Json.ToObjectAsync<SongSearchObject>(item.Value);
                if (song == null) continue;
                sortedList.Add((item.Key, song));
            }

            foreach (var pair in sortedList.OrderBy(pair => pair.Item2.QueueCounter))
            {
                string hash = pair.Item1;
                SongSearchObject song = pair.Item2;

                // If local, set the local bitmap image
                if (song.Source == "local")
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
        } catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }
}