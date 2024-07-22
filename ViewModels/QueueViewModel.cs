using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;

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
    public string? DownloadPath
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
        DownloadPath = "test";
    }
}

public partial class QueueViewModel : ObservableRecipient
{
    public static ObservableCollection<QueueObject> Source
    {
        get;
        set;
    } = new ObservableCollection<QueueObject>();

    private static HashSet<string> trackSet = new HashSet<string>();

    public QueueViewModel()
    {
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

    public void RunCommand(string command)
    {
        Thread thread = new Thread(() =>
        {
            foreach (var item in Source)
            {
                TerminalSubprocess.RunCommandSync(command);
            }
        });
        thread.Start();
    }
}