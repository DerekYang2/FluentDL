using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private static bool isRunning = false;

    public QueueViewModel()
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
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
    }

    private static string GetHash(SongSearchObject song)
    {
        return song.Source + song.Id;
    }

    public static void RunCommand(string command, CancellationToken token)
    {
        if (isRunning)
        {
            return;
        }


        Thread thread = new Thread(() =>
        {
            isRunning = true;
            for (int i = 0; i < Source.Count; i++)
            {
                if (token.IsCancellationRequested) // Break the loop if the token is cancelled
                {
                    isRunning = false;
                    return;
                }

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

                var newObj = Source[i];
                newObj.ResultString = TerminalSubprocess.GetRunCommandSync(thisCommand);
                Debug.WriteLine(newObj.ResultString);

                // Capture the current value of i
                int currentIndex = i;

                dispatcher.TryEnqueue(() =>
                {
                    Source[currentIndex] = newObj; // This actually refreshes the UI of ObservableCollection
                });
            }

            isRunning = false;
        });
        thread.Start();
    }
}