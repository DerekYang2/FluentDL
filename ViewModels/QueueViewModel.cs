using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;

namespace FluentDL.ViewModels;

public partial class QueueViewModel : ObservableRecipient
{
    public ObservableCollection<SongSearchObject> Source
    {
        get;
        set;
    } = new ObservableCollection<SongSearchObject>();

    public QueueViewModel()
    {
    }
}