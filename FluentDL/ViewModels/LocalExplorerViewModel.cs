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
using CommunityToolkit.WinUI.UI.Controls;
using FFMpegCore.Builders.MetaData;
using FluentDL.Helpers;
using Microsoft.UI.Xaml.Media.Imaging;
using FluentDL.Models;
using Microsoft.UI.Xaml;
using TagLib;
using File = TagLib.File;

namespace FluentDL.ViewModels;

public partial class LocalExplorerViewModel : ObservableRecipient
{
    private ILocalSettingsService localSettings;
    private string currentEditPath = ""; // Path of the file currently being edited
    private static Dictionary<string, MetadataObject> tmpUpdates = new Dictionary<string, MetadataObject>();

    public ObservableCollection<MetadataPair> CurrentMetadataList
    {
        get;
        set;
    }

    public Visibility AddVisibility
    {
        get;
        set;
    }

    public Visibility EditVisibility
    {
        get;
        set;
    }

    public Visibility OpenVisibility
    {
        get;
        set;
    }

    public LocalExplorerViewModel()
    {
        localSettings = App.GetService<ILocalSettingsService>();
    }

    public async Task InitializeAsync()
    {
        AddVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.LocalExplorerAddChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        EditVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.LocalExplorerEditChecked) == true ? Visibility.Visible : Visibility.Collapsed;
        OpenVisibility = await localSettings.ReadSettingAsync<bool?>(SettingsViewModel.LocalExplorerOpenChecked) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public string GetCurrentEditPath()
    {
        return currentEditPath;
    }

    public void SetUpdateObject(SongSearchObject song)
    {
        currentEditPath = song.Id;

        if (!tmpUpdates.ContainsKey(currentEditPath)) // Should not happen
        {
            throw new KeyNotFoundException("The file path does not exist in the dictionary.");
        }

        CurrentMetadataList = tmpUpdates[currentEditPath].GetMetadataPairCollection(); // Set the metadata list for the current file
    }

    public string? GetCurrentImagePath()
    {
        return tmpUpdates[currentEditPath].AlbumArtPath;
    }

    public void SetImagePath(string path)
    {
        tmpUpdates[currentEditPath].AlbumArtPath = path; // Set the image path for the current file
    }

    public async Task SaveMetadata()
    {
        tmpUpdates[currentEditPath].SetFields(CurrentMetadataList); // Apply metadata updates to the object
        await tmpUpdates[currentEditPath].SaveAsync(); // Save the metadata to the file
        //App.AddMetadataUpdate(currentEditPath, tmpUpdates[currentEditPath]); // Add the metadata update to the list of pending updates
    }

    public static string? GetISRC(TagLib.File track)
    {
        if (track.Tag.ISRC != null)
        {
            return track.Tag.ISRC;
        }

        // Attempt to get from filename
        var fileName = track.Name;
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

    public static MetadataObject? GetMetadataObject(string path)
    {
        return tmpUpdates.GetValueOrDefault(path);
    }

    public static SongSearchObject? ParseFile(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        if (!tmpUpdates.TryGetValue(path, out var value)) // Add the file to the dictionary if it does not exist
        {
            try
            {
                value = new MetadataObject(path); // Create a new metadata object
            }
            catch (Exception e)
            {
                Debug.WriteLine("Could not parse file: " + e.Message);
                return null;
            }

            tmpUpdates[path] = value; // Add the metadata object to the dictionary
        }

        var metadataObj = value;

        var fileName = Path.GetFileNameWithoutExtension(path);
        return new SongSearchObject()
        {
            Source = "local",
            Id = path,
            Title = metadataObj.Title ?? fileName,
            Artists = string.Join(", ", metadataObj.Artists ?? new string[] { "N/A" }),
            AlbumName = metadataObj.AlbumName ?? "N/A",
            Duration = metadataObj.Duration.ToString(),
            ReleaseDate = metadataObj.ReleaseDate?.ToString("yyyy-MM-dd") ?? "",
            TrackPosition = (metadataObj.TrackNumber ?? 1).ToString(),
            Explicit = (metadataObj.Title ?? "").ToLower().Contains("explicit") || fileName.ToLower().Contains("[e]"),
            Rank = "0",
            ImageLocation = null,
            LocalBitmapImage = null,
            Isrc = metadataObj.Isrc,
            AudioFormat = metadataObj.Codec ?? Path.GetExtension(path)
        };
    }

    //public static BitmapImage? GetBitmapImage(Track track)
    //{
    //    System.Collections.Generic.IList<PictureInfo> embeddedPictures = track.EmbeddedPictures;
    //    if (embeddedPictures.Count > 0)
    //    {
    //        var firstImg = embeddedPictures[0];
    //        // Create bitmap image from byte array
    //        var bitmapImage = new BitmapImage();
    //        using (var stream = new MemoryStream(firstImg.PictureData))
    //        {
    //            bitmapImage.SetSource(stream.AsRandomAccessStream());
    //        }

    //        return bitmapImage;
    //    }

    //    return null;
    //}

    public static async Task<BitmapImage?> GetBitmapImageAsync(string filePath)
    {
        try {
            using var memoryStream = await Task.Run(() => GetAlbumArtMemoryStream(filePath));
        
            if (memoryStream != null) // Set album art if available
            {
                var bitmapImage = new BitmapImage
                {
                    DecodePixelHeight = 76, // No need to set height, aspect ratio is automatically handled
                };
                await bitmapImage.SetSourceAsync(memoryStream.AsRandomAccessStream());
                return bitmapImage;
            }
        } catch (Exception e) {
            Debug.WriteLine("Could not get bitmap image: " + e.Message);
        }
        return null;
    }


    public static byte[]? GetAlbumArtBytes(string filePath)
    {
        if (!tmpUpdates.TryGetValue(filePath, out var value))
        {
            throw new KeyNotFoundException("The file path does not exist in the dictionary.");
        }

        return value.GetAlbumArt();
    }

    public static MemoryStream? GetAlbumArtMemoryStream(string filePath)
    {
        if (!tmpUpdates.TryGetValue(filePath, out var value))
        {
            throw new KeyNotFoundException("The file path does not exist in the dictionary.");
        }

        var byteArr = value.GetAlbumArt();
        if (byteArr == null)
        {
            return null;
        }

        return new MemoryStream(byteArr);
    }
}