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

    private Track currentTrack;

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
            new() { Key = "Title", Value = currentTrack.Title },
            new() { Key = "Contributing artists", Value = currentTrack.Artist },
            new() { Key = "Genre", Value = currentTrack.Genre },
            new() { Key = "Album", Value = currentTrack.Album },
            new() { Key = "Album artist", Value = currentTrack.AlbumArtist },
            new() { Key = "ISRC", Value = currentTrack.ISRC },
            new() { Key = "BPM", Value = currentTrack.BPM.ToString() },
            new() { Key = "Date", Value = currentTrack.Date.ToString() },
            new() { Key = "Year", Value = currentTrack.Year.ToString() },
            new() { Key = "Track number", Value = currentTrack.TrackNumber.ToString() },
            new() { Key = "Track total", Value = currentTrack.TrackTotal.ToString() },
        };

        foreach (var pair in currentTrack.AdditionalFields)
        {
            MetadataList.Add(new MetadataPair() { Key = pair.Key, Value = pair.Value });
        }
    }

    public async Task<bool> SaveMetadata()
    {
        foreach (var pair in MetadataList) // Update the track with the new metadata
        {
            if (pair.Key == "Title")
            {
                currentTrack.Title = pair.Value;
            }
            else if (pair.Key == "Contributing artists")
            {
                currentTrack.Artist = pair.Value;
            }
            else if (pair.Key == "Genre")
            {
                currentTrack.Genre = pair.Value;
            }
            else if (pair.Key == "Album")
            {
                currentTrack.Album = pair.Value;
            }
            else if (pair.Key == "Album artist")
            {
                currentTrack.AlbumArtist = pair.Value;
            }
            else if (pair.Key == "ISRC")
            {
                currentTrack.ISRC = pair.Value;
            }
            else if (pair.Key == "BPM" && !string.IsNullOrWhiteSpace(pair.Value))
            {
                if (int.TryParse(pair.Value, out var bpm))
                {
                    currentTrack.BPM = bpm;
                }
            }
            else if (pair.Key == "Date" && !string.IsNullOrWhiteSpace(pair.Value))
            {
                if (DateTime.TryParse(pair.Value, out var date))
                {
                    currentTrack.Date = date;
                }
            }
            else if (pair.Key == "Year" && !string.IsNullOrWhiteSpace(pair.Value))
            {
                if (int.TryParse(pair.Value, out var year))
                {
                    currentTrack.Year = year;
                }
            }
            else if (pair.Key == "Track number" && !string.IsNullOrWhiteSpace(pair.Value))
            {
                if (int.TryParse(pair.Value, out var trackNumber))
                {
                    currentTrack.TrackNumber = trackNumber;
                }
            }
            else if (pair.Key == "Track total" && !string.IsNullOrWhiteSpace(pair.Value))
            {
                if (int.TryParse(pair.Value, out var trackTotal))
                {
                    currentTrack.TrackTotal = trackTotal;
                }
            }
            else
            {
                currentTrack.AdditionalFields[pair.Key] = pair.Value;
            }
        }

        return await currentTrack.SaveAsync();
    }

    public static SongSearchObject? ParseFile(string path)
    {
        var track = new Track(path);

        return new SongSearchObject()
        {
            Source = "local",
            Id = path,
            Title = track.Title,
            Artists = track.Artist.Replace(";", ", ").Replace("; ", ", "),
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