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
using Microsoft.UI.Xaml.Media.Imaging;

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
            Rank = "0",
            LocalBitmapImage = GetBitmapImage(track)
        };
    }

    public static BitmapImage? GetBitmapImage(Track track)
    {
        System.Collections.Generic.IList<PictureInfo> embeddedPictures = track.EmbeddedPictures;
        if (embeddedPictures.Count > 0)
        {
            var firstImg = embeddedPictures[0];
            App.MainWindow.Dispatcher 
            // Create bitmap image from byte array
            var bitmapImage = new BitmapImage();
            using (var stream = new MemoryStream(firstImg.PictureData))
            {
                bitmapImage.SetSource(stream.AsRandomAccessStream());
            }

            return bitmapImage;
        }

        return null;
    }
}