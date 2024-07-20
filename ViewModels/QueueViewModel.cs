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
    public static ObservableCollection<SongSearchObject> Source = new ObservableCollection<SongSearchObject>();

    public QueueViewModel()
    {
    }

    public static void Add(SongSearchObject song)
    {
        Source.Add(song);
    }
}