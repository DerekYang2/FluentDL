using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using ABI.Microsoft.UI.Xaml;
using AngleSharp.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;
using ATL.AudioData;
using ATL;
using ATL.Logging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentDL.ViewModels;

public class MetadataPair
{
    public string? Key
    {
        get;
        set;
    }

    public string? Value
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

    private Track currentTrack = new Track();
    private string imgPath = "";

    public LocalExplorerViewModel()
    {
    }


    public void SetMetadataList(SongSearchObject song)
    {
        // Get all metadata from the song object
        currentTrack = new Track(song.Id);

        // Only include settable metadata fields
        MetadataList = new ObservableCollection<MetadataPair>()
        {
            new() { Key = "Title", Value = currentTrack.Title ?? "" },
            new() { Key = "Contributing artists", Value = currentTrack.Artist ?? "" },
            new() { Key = "Genre", Value = currentTrack.Genre ?? "" },
            new() { Key = "Album", Value = currentTrack.Album ?? "" },
            new() { Key = "Album artist", Value = currentTrack.AlbumArtist ?? "" },
            new() { Key = "ISRC", Value = currentTrack.ISRC ?? "" },
            new() { Key = "BPM", Value = (currentTrack.BPM ?? 0).ToString() },
            new() { Key = "Date", Value = (currentTrack.Date ?? new DateTime()).ToString() },
            new() { Key = "Track number", Value = (currentTrack.TrackNumber ?? 0).ToString() },
            new() { Key = "Track total", Value = (currentTrack.TrackTotal ?? 0).ToString() },
        };

        foreach (var pair in currentTrack.AdditionalFields)
        {
            MetadataList.Add(new MetadataPair() { Key = pair.Key, Value = pair.Value });
        }
    }

    public void SetImagePath(string path)
    {
        imgPath = path;
    }

    public void SaveMetadata()
    {
        var metadataJson = new MetadataJson() { Path = currentTrack.Path, MetadataList = MetadataList.ToList(), ImagePath = imgPath };
        App.AddMetadataUpdate(metadataJson);
    }

    public static SongSearchObject? ParseFile(string path)
    {
        var track = new Track(path);

        return new SongSearchObject()
        {
            Source = "local",
            Id = path,
            Title = track.Title,
            Artists = track.Artist.Replace("; ", ", ").Replace(";", ", "),
            AlbumName = track.Album,
            Duration = track.Duration.ToString(),
            ReleaseDate = track.Date.ToString().Substring(0, 10),
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
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
            using var stream = new MemoryStream(firstImg.PictureData);
            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
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