using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;

namespace FluentDL.ViewModels;

public partial class QueueViewModel : ObservableRecipient
{
    public static ObservableCollection<SongSearchObject> Source
    {
        get;
        set;
    } = new ObservableCollection<SongSearchObject>();

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

        Source.Add(song);
        trackSet.Add(GetHash(song));
    }

    public static void Remove(SongSearchObject song)
    {
        Source.Remove(song);
        trackSet.Remove(GetHash(song));
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
}