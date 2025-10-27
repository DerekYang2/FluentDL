using ABI.Microsoft.UI.Dispatching;
using Acornima.Ast;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Core.Models;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Views;
using Jint.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Windows.Storage;
using Windows.Storage.Streams;
using static FluentDL.Views.QueuePage;
using static System.Net.WebRequestMethods;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.ViewModels;

public class QueueObject : SongSearchObject
{    
    [JsonIgnore]
    public string? ResultString
    {
        get;
        set;
    }

    [JsonIgnore]
    public bool IsRunning
    {
        get;
        set;
    }

    [JsonIgnore]
    public SolidColorBrush ConvertBadgeColor
    {
        get;
        set;
    } = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

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

        this.ShareVisibility = ShareVisibility;
        this.DownloadCoverVisibility = DownloadCoverVisibility;
        this.RemoveVisibility = RemoveVisibility;
    }
}

public partial class QueueViewModel : ObservableRecipient, INotifyPropertyChanged
{
    public static ObservableCollection<QueueObject> Source
    {
        get;
        set;
    } = new ObservableCollection<QueueObject>();

    private int _successCount;

    public int SuccessCount
    {
        get => _successCount;
        set => SetField(ref _successCount, value, nameof(SuccessCount));
    }

    private int _warningCount;

    public int WarningCount
    {
        get => _warningCount;
        set => SetField(ref _warningCount, value, nameof(WarningCount));
    }

    private int _errorCount;

    public int ErrorCount
    {
        get => _errorCount;
        set => SetField(ref _errorCount, value, nameof(ErrorCount));
    }

    // boiler-plate
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChangedEventHandler handler = PropertyChanged;
        if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static string command
    {
        get;
        set;
    } = string.Empty;

    private static ILocalSettingsService localSettings = App.GetService<ILocalSettingsService>();


    public static DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private static HashSet<string> trackSet = new HashSet<string>();
    private static int index, completedCount;

    private static readonly object _lock = new object();
    private static bool _isRunning = false;

    public static bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isRunning;
            }
        }
        set
        {
            lock (_lock)
            {
                _isRunning = value;
            }
        }
    }

    public static Visibility ShareVisibility = Visibility.Collapsed;
    public static Visibility DownloadCoverVisibility = Visibility.Collapsed;
    public static Visibility RemoveVisibility = Visibility.Collapsed;


    public QueueViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
        dispatcher = DispatcherQueue.GetForCurrentThread();
        index = 0;
        completedCount = 0;
    }

    public static async Task UpdateShortcutVisibility() {
        ShareVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueShareChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        DownloadCoverVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueDownloadCoverChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        RemoveVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.QueueRemoveChecked) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public static async Task<IRandomAccessStream?> GetRandomAccessStreamFromUrl(string uri)
    {
        try
        {
            var client = new HttpClient();
            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var inputStream = await response.Content.ReadAsStreamAsync();
            return inputStream.AsRandomAccessStream();
        }
        catch (Exception e)
        {
            Debug.WriteLine("ERROR CREATING STREAM: " + e.Message);
            return null;
        }
    }

    public static async Task<QueueObject?> CreateQueueObject(SongSearchObject song)
    {
        var queueObj = new QueueObject(song, ShareVisibility, DownloadCoverVisibility, RemoveVisibility);   

        if (queueObj.LocalBitmapImage == null) // Create a local bitmap image for queue objects to prevent disappearing listview images
        {
            using var memoryStream = await GetRandomAccessStreamFromUrl(queueObj.ImageLocation); // Get the memory stream from the url

            if (memoryStream == null) return null;


            var bitmapImage = new BitmapImage { DecodePixelHeight = 76 }; // Create a new bitmap image

            // Set the source of the bitmap image
            await bitmapImage.SetSourceAsync(memoryStream);

            queueObj.LocalBitmapImage = bitmapImage; // Set the local bitmap image
        }

        return queueObj;
    }

    public static void Add(SongSearchObject? song)
    {
        if (song == null || trackSet.Contains(GetHash(song)))
        {
            return;
        }

        var queueObj = new QueueObject(song, ShareVisibility, DownloadCoverVisibility, RemoveVisibility);
        Source.Add(queueObj);
        trackSet.Add(GetHash(song));

        if (dispatcher == null)
        {
            dispatcher = DispatcherQueue.GetForCurrentThread();
        }

        Thread t = new Thread(async () =>
        {
            if (queueObj.LocalBitmapImage == null) // Create a local bitmap image for queue objects to prevent disappearing listview images
            {
                var memoryStream = await GetRandomAccessStreamFromUrl(queueObj.ImageLocation); // Get the memory stream from the url

                if (memoryStream == null) return;


                dispatcher.TryEnqueue(() =>
                {
                    var bitmapImage = new BitmapImage { DecodePixelHeight = 76 }; // Create a new bitmap image

                    // Set the source of the bitmap image
                    bitmapImage.SetSourceAsync(memoryStream).Completed += (info, status) =>
                    {
                        queueObj.LocalBitmapImage = bitmapImage; // Set the local bitmap image
                        Source[Source.IndexOf(queueObj)] = queueObj; // Refresh the UI
                    };
                });
            }
        });
        t.Start();
    }

    public static void Remove(SongSearchObject song)
    {
        var hash = GetHash(song);
        trackSet.Remove(hash);
        Source.Remove(Source.First(x => GetHash(x) == hash));
    }

    public static void Replace(int index, QueueObject? queueObj)
    {
        if (queueObj == null) return;

        trackSet.Remove(GetHash(Source[index])); // Remove old from trackset
        Source[index] = queueObj; // SEt to new object
        trackSet.Add(GetHash(Source[index])); // Add new hash to trackset
    }

    public static void Clear()
    {
        Source.Clear();
        trackSet.Clear();
        IsRunning = false;
        index = 0;
        completedCount = 0;
    }

    public static void Reset()
    {
        index = 0; // Reset the index
        completedCount = 0;
        IsRunning = false;

        for (int i = 0; i < Source.Count; i++) // Remove all result str, set new obj to refresh ui
        {
            var cleanObj = Source[i];
            cleanObj.ResultString = null;
            Source[i] = cleanObj;
        }
    }

    public static void ResetConversionResults()
    {
        for (int i = 0; i < Source.Count; i++) // Remove all result str, set new obj to refresh ui
        {
            var cleanObj = Source[i];
            cleanObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); // Reset the badge color
            Source[i] = cleanObj;
        }
    }

    private static string GetHash(SongSearchObject song)
    {
        return song.Source + song.Id;
    }

    public static void SetCommand(string newCommand)
    {
        command = newCommand;
    }

    public static async Task RunCommand(string? directory, CancellationToken token, QueuePage.QueueRunCallback callback)
    {
        if (IsRunning || index >= Source.Count)
        {
            return;
        }

        IsRunning = true; // Start running 

        int threadCount = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.CommandThreads) ?? 1;

        for (int t = 0; t < threadCount; t++) // Edit this to change the number of threads
        {
            Thread thread = new Thread(() =>
            {
                while (IsRunning) // Multithreaded loop
                {
                    if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                    {
                        IsRunning = false;
                        callback.Invoke(InfoBarSeverity.Informational, "Command running paused.");
                        return;
                    }

                    var i = Interlocked.Increment(ref index) - 1; // Increment the index in a thread-safe manner

                    if (i >= Source.Count)
                    {
                        return;
                    }

                    // Update the is running of the current object
                    dispatcher.TryEnqueue(() =>
                    {
                        var newObj = Source[i];
                        newObj.IsRunning = true;
                        Source[i] = newObj;
                    });

                    var isLocal = Source[i].Source.Equals("local");
                    // Get the url of the current object
                    var url = ApiHelper.GetUrl(Source[i]);

                    var thisCommand = command.Replace("{url}", url)
                        .Replace("{ext}", (isLocal ? Path.GetExtension(Source[i].Id) : ".").Substring(1))
                        .Replace("{file_name}", isLocal ? Path.GetFileName(Source[i].Id) : "")
                        .Replace("{title}", Source[i].Title)
                        .Replace("{image_url}", Source[i].ImageLocation ?? "")
                        .Replace("{id}", isLocal ? "" : Source[i].Id)
                        .Replace("{release_date}", Source[i].ReleaseDate)
                        .Replace("{artists}", Source[i].Artists)
                        .Replace("{duration}", Source[i].Duration)
                        .Replace("{album}", Source[i].AlbumName)
                        .Replace("{isrc}", Source[i].Isrc);

                    // Run the command
                    var resultStr = TerminalSubprocess.GetRunCommandSync(thisCommand, directory);

                    // Update the actual object
                    dispatcher.TryEnqueue(() =>
                    {
                        try {
                            var newObj = Source[i];
                            newObj.ResultString = resultStr;
                            newObj.IsRunning = false;
                            Source[i] = newObj; // This actually refreshes the UI of ObservableCollection
                        } catch (Exception e) {
                            Debug.WriteLine("error updating UI for command runner");  
                        }
                    });

                    var capturedCount = Interlocked.Increment(ref completedCount);
                    if (capturedCount == Source.Count) // Check if all completed
                    {
                        IsRunning = false;
                        callback.Invoke(InfoBarSeverity.Success, "Command running complete.");
                    }
                }
            });
            thread.Start();
        }
    }

    public static int GetCompletedCount()
    {
        int completed = 0;
        foreach (var item in Source)
        {
            if (item.ResultString != null)
            {
                completed++;
            }
        }

        return completed;
    }

    public override string ToString() {
        return JsonConvert.SerializeObject(Source.Select(x => x.ToString()).ToList()); // Convert to json string
    }

    public static async Task SaveQueue() {
        await Task.Run(()=>{
            try {
                var saveString = JsonConvert.SerializeObject(Source.Select(x => x.ToString()).ToList()); // Convert to json string
                QueueSaver.SaveString(saveString); // Save the string to the 
            } catch (Exception e) {
                Debug.WriteLine("Error saving queue: " + e.Message); // Log the error
            }
        });
    }

    public static async Task LoadSaveQueue() {
        string path = QueueSaver.GetPath(); // Get the path of the file
        try {
            if (System.IO.File.Exists(path)) {
                var saveString = await System.IO.File.ReadAllTextAsync(path); // Read the file
                await LoadSaveQueue(saveString); // Load the queue from the file
            } else {
                // Create the file if it does not exist
                using (var fileStream = System.IO.File.Create(path)) {
                    // File created
                }
            }
        } catch (Exception e) {
            Debug.WriteLine("Error loading queue: " + e.Message); // Log the error
        }
    }
    private static async Task LoadSaveQueue(string saveString) {
        if (string.IsNullOrEmpty(saveString)) return; // Check if the string is empty
   
        var listJsonStr = JsonConvert.DeserializeObject<List<string>>(saveString);
        
        if (listJsonStr == null) return;
        foreach (var songStr in listJsonStr) {
            var song = await Json.ToObjectAsync<SongSearchObject>(songStr); // Deserialize the string to a SongSearchObject
            if (song == null) continue;
            // If local, set the local bitmap image
            if (song.Source == "local") {
                var localSong = LocalExplorerViewModel.ParseFile(song.Id);  // Re-parse the file
                if (localSong != null)
                {
                    localSong.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(song.Id); // Get the bitmap image from the file
                    Add(localSong);
                }
            } else {
                Add(song);
            }
        }
    }


}