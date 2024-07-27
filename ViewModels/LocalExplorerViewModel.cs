using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using ABI.Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;
using ATL.AudioData;
using ATL;

namespace FluentDL.ViewModels;

public partial class LocalExplorerViewModel : ObservableRecipient
{
    public LocalExplorerViewModel()
    {
    }

    public static SongSearchObject? ParseFile(string path)
    {
        var track = new Track(path);
        var artistCsv = track.AdditionalFields.TryGetValue("Contributing artists", out var artists) ? artists : track.Artist;
        artistCsv = artistCsv.Replace(";", ", ");

        Debug.WriteLine("ARTIST CSV: " + artistCsv);
        Debug.WriteLine("Duration: " + track.Duration.ToString());
        Debug.WriteLine("Release: " + (track.Date).ToString().Substring(0, 10));

        return new SongSearchObject()
        {
            Source = "local",
            Id = path,
            Title = track.Title,
            Artists = artistCsv,
            AlbumName = track.Album,
            Duration = track.Duration.ToString(),
            ReleaseDate = track.Date.ToString().Substring(0, 10),
            TrackPosition = "1",
            Explicit = track.Title.ToLower().Contains("explicit") || track.Title.ToLower().Contains("[e]"),
            ImageLocation = "test.png",
            Rank = "0"
        };
    }
}