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

public class MetadataPair
{
    public string Key
    {
        get;
        set;
    }

    public string Value
    {
        get;
        set;
    }
}

public partial class LocalExplorerViewModel : ObservableRecipient
{
    public ObservableCollection<MetadataPair> MetadataList
    {
        get;
        set;
    } = new ObservableCollection<MetadataPair>();

    public LocalExplorerViewModel()
    {
    }

    public void SetMetadataList()
    {
        // test
        MetadataList = new ObservableCollection<MetadataPair>
        {
            new MetadataPair { Key = "Title", Value = "Test Title" },
            new MetadataPair { Key = "Artist", Value = "Test Artist" },
            new MetadataPair { Key = "Album", Value = "Test Album" },
            new MetadataPair { Key = "Duration", Value = "Test Duration" },
            new MetadataPair { Key = "Release Date", Value = "Test Release Date" },
            new MetadataPair { Key = "Track Position", Value = "Test Track Position" },
            new MetadataPair { Key = "Explicit", Value = "Test Explicit" },
            new MetadataPair { Key = "Rank", Value = "Test Rank" }
        };
    }

    public static SongSearchObject? ParseFile(string path)
    {
        var track = new Track(path);
        var artistCsv = track.AdditionalFields.TryGetValue("Contributing artists", out var artists) ? artists : track.Artist;
        artistCsv = artistCsv.Replace(";", ", ");

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
            ImageLocation = null,
            LocalBitmapImage = null
        };
    }

    public static BitmapImage? GetBitmapImage(Track track)
    {
        System.Collections.Generic.IList<PictureInfo> embeddedPictures = track.EmbeddedPictures;
        if (embeddedPictures.Count > 0)
        {
            var firstImg = embeddedPictures[0];
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

    public static async Task<BitmapImage?> GetBitmapImageAsync(Track track)
    {
        System.Collections.Generic.IList<PictureInfo> embeddedPictures = track.EmbeddedPictures;
        if (embeddedPictures.Count > 0)
        {
            var firstImg = embeddedPictures[0];
            // Create bitmap image from byte array
            var bitmapImage = new BitmapImage();
            using (var stream = new MemoryStream(firstImg.PictureData))
            {
                await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
            }

            return bitmapImage;
        }

        return null;
    }

    public static MemoryStream? GetAlbumArtMemoryStream(SongSearchObject song)
    {
        var track = new Track(song.Id);

        System.Collections.Generic.IList<PictureInfo> embeddedPictures = track.EmbeddedPictures;
        if (embeddedPictures.Count > 0)
        {
            var firstImg = embeddedPictures[0];
            return new MemoryStream(firstImg.PictureData);
        }

        return null;
    }
}