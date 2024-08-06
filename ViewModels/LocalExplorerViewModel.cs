using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using FluentDL.Models;

namespace FluentDL.ViewModels;

public partial class LocalExplorerViewModel : ObservableRecipient
{
    private string currentEditPath = ""; // Path of the file currently being edited
    private Dictionary<string, MetadataUpdateInfo> tmpUpdates = new Dictionary<string, MetadataUpdateInfo>();

    public ObservableCollection<MetadataPair> CurrentMetadataList
    {
        get;
        set;
    }

    public LocalExplorerViewModel()
    {
    }


    public void SetUpdateObject(SongSearchObject song)
    {
        currentEditPath = song.Id;

        if (!tmpUpdates.ContainsKey(currentEditPath)) // If the file is not already in the map, add it
        {
            // Get all metadata from the song object
            var currentTrack = new Track(song.Id);

            // Only include settable metadata fields
            var newList = new ObservableCollection<MetadataPair>()
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
                if (pair.Key.Equals("YEAR")) continue; // This is a special field set by this program, do not edit it
                newList.Add(new MetadataPair() { Key = pair.Key, Value = pair.Value });
            }

            tmpUpdates.Add(currentEditPath, new MetadataUpdateInfo(newList, currentEditPath, ""));
        }

        CurrentMetadataList = tmpUpdates[currentEditPath].GetMetadataList(); // Set the metadata list for the current file
    }

    public string? GetCurrentImagePath()
    {
        return tmpUpdates[currentEditPath].GetImagePath();
    }

    public void SetImagePath(string path)
    {
        tmpUpdates[currentEditPath].SetImagePath(path); // Set the image path for the current file
    }

    public void SaveMetadata()
    {
        App.metadataUpdates.Add(currentEditPath, tmpUpdates[currentEditPath]); // Add the metadata update to the list of pending updates
    }

    public void DiscardMetadata()
    {
        tmpUpdates.Remove(currentEditPath);
    }

    public static string? GetISRC(Track track)
    {
        if (track.ISRC != null)
        {
            return track.ISRC;
        }

        // Attempt to get from filename
        var fileName = Path.GetFileName(track.Path);
        if (fileName != null)
        {
            var isrc = Regex.Match(fileName, @"[A-Z]{2}[A-Z0-9]{3}\d{2}\d{5}").Value;
            if (isrc.Length == 12)
            {
                return isrc;
            }
        }

        return null;
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
            LocalBitmapImage = null,
            Isrc = GetISRC(track),
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
        try
        {
            if (embeddedPictures.Count > 0)
            {
                var firstImg = embeddedPictures[0];
                // Create bitmap image from byte array
                using var stream = new MemoryStream(firstImg.PictureData);
                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                stream.Close();
                return bitmapImage;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
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