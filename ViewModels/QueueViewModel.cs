using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ABI.Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;
using static System.Net.WebRequestMethods;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.ViewModels;

/*
 *    public string Title
   {
       get;
       set;
   }

   public string ImageLocation
   {
       get;
       set;
   }

   public string Id
   {
       get;
       set;
   }

   public string ReleaseDate
   {
       get;
       set;
   }

   public string Artists
   {
       get;
       set;
   }

   public string Duration
   {
       get;
       set;
   }

   public string Rank
   {
       get;
       set;
   }

   public string AlbumName
   {
       get;
       set;
   }

   public string Source
   {
       get;
       set;
   }

   public bool Explicit
   {
       get;
       set;
   }

 */
public class QueueObject : SongSearchObject
{
    public string? ResultString
    {
        get;
        set;
    }

    public bool IsRunning
    {
        get;
        set;
    }

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
        ResultString = null;
        IsRunning = false;
    }
}

public partial class QueueViewModel : ObservableRecipient
{
    public static ObservableCollection<QueueObject> Source
    {
        get;
        set;
    } = new ObservableCollection<QueueObject>();

    private static DispatcherQueue dispatcher;
    private static HashSet<string> trackSet = new HashSet<string>();
    private static int index;

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

    public QueueViewModel()
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        index = 0;
    }

    public static void Add(SongSearchObject song)
    {
        if (trackSet.Contains(GetHash(song)))
        {
            return;
        }

        Source.Add(new QueueObject(song));
        trackSet.Add(GetHash(song));
    }

    public static void Remove(SongSearchObject song)
    {
        var hash = GetHash(song);
        trackSet.Remove(hash);
        Source.Remove(Source.First(x => GetHash(x) == hash));
    }

    public static void Clear()
    {
        Source.Clear();
        trackSet.Clear();
        IsRunning = false;
        index = 0;
    }

    private static string GetHash(SongSearchObject song)
    {
        return song.Source + song.Id;
    }

    public static void RunCommand(string command, string? directory, CancellationToken token)
    {
        if (IsRunning)
        {
            return;
        }

        // In case command was already run before, cleanup first
        index = 0; // Reset index
        for (int i = 0; i < Source.Count; i++) // Remove all result str, set new obj to refresh ui
        {
            var cleanObj = Source[i];
            cleanObj.ResultString = null;
            Source[i] = cleanObj;
        }


        IsRunning = true; // Start running

        Thread thread = new Thread(() =>
        {
            for (int i = 0; i < Source.Count; i++)
            {
                int curIdx = i; // Capture the current index
                if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                {
                    break;
                }

                // Update the is running of the current object
                dispatcher.TryEnqueue(() =>
                {
                    var newObj = Source[curIdx];
                    newObj.IsRunning = true;
                    Source[curIdx] = newObj;
                });

                // Get the url of the current object
                string url;
                switch (Source[i].Source)
                {
                    case "deezer":
                        url = "https://www.deezer.com/track/" + Source[i].Id;
                        break;
                    case "youtube":
                        url = "https://www.youtube.com/watch?v=" + Source[i].Id;
                        break;
                    default:
                        url = string.Empty;
                        break;
                }

                var thisCommand = command.Replace("%title%", Source[i].Title).Replace("%artist%", Source[i].Artists).Replace("%url%", url);
                // Run the command
                var resultStr = TerminalSubprocess.GetRunCommandSync(thisCommand, directory);
                Debug.WriteLine(resultStr);

                // Update the actual object
                dispatcher.TryEnqueue(() =>
                {
                    var newObj = Source[curIdx];
                    newObj.ResultString = resultStr;
                    newObj.IsRunning = false;
                    Source[curIdx] = newObj; // This actually refreshes the UI of ObservableCollection
                });
            }

            IsRunning = false;
        });
        thread.Start();
    }

    // Use for multithreaded run
    private static void MultiThreadedRun(string command, string? directory, CancellationToken token)
    {
        IsRunning = true; // Start running
        for (int t = 0; t < 1; t++) // Edit this to change the number of threads
        {
            Thread thread = new Thread(() =>
            {
                while (IsRunning) // Multithreaded loop
                {
                    if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                    {
                        IsRunning = false;
                        return;
                    }

                    int i = index;
                    Interlocked.Increment(ref index); // Increment the index in a thread-safe manner

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

                    // Get the url of the current object
                    string url;
                    switch (Source[i].Source)
                    {
                        case "deezer":
                            url = "https://www.deezer.com/track/" + Source[i].Id;
                            break;
                        case "youtube":
                            url = "https://www.youtube.com/watch?v=" + Source[i].Id;
                            break;
                        default:
                            url = string.Empty;
                            break;
                    }

                    var thisCommand = command.Replace("%title%", Source[i].Title).Replace("%artist%", Source[i].Artists).Replace("%url%", url);
                    // Run the command
                    var resultStr = TerminalSubprocess.GetRunCommandSync(thisCommand, directory);

                    // Update the actual object
                    dispatcher.TryEnqueue(() =>
                    {
                        var newObj = Source[i];
                        newObj.ResultString = resultStr;
                        newObj.IsRunning = false;
                        Source[i] = newObj; // This actually refreshes the UI of ObservableCollection
                        if (GetCompletedCount() == Source.Count) // Check if all completed
                        {
                            IsRunning = false;
                        }
                    });
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
}