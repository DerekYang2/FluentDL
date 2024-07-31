using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ATL;

namespace FluentDL.Helpers;

internal class MetadataJsonHelper
{
    public static bool SaveTrack(JsonObject jsonObj)
    {
        var path = jsonObj["Path"].GetValue<string>();
        var imagePath = jsonObj["ImagePath"].GetValue<string>();

        // Check if path still exists 
        if (!File.Exists(path))
        {
            return false;
        }

        var track = new Track(path);

        if (File.Exists(imagePath)) // Save image if it exists
        {
            PictureInfo newPicture = PictureInfo.fromBinaryData(System.IO.File.ReadAllBytes(imagePath), PictureInfo.PIC_TYPE.Front);
            // Append to front if pictures already exist
            if (track.EmbeddedPictures.Count > 0)
            {
                //track.EmbeddedPictures.RemoveAt(0);
                track.EmbeddedPictures.Insert(0, newPicture);
            }
            else
            {
                track.EmbeddedPictures.Add(newPicture);
            }
        }

        foreach (var pair in jsonObj["MetadataList"].AsArray())
        {
            if (pair["Key"].GetValue<string>() == "Title")
            {
                track.Title = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Contributing artists")
            {
                track.Artist = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Genre")
            {
                track.Genre = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Album")
            {
                track.Album = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "Album artist")
            {
                track.AlbumArtist = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "ISRC")
            {
                track.ISRC = pair["Value"].GetValue<string>();
            }
            else if (pair["Key"].GetValue<string>() == "BPM" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var bpm))
                {
                    track.BPM = bpm;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Date" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (DateTime.TryParse(pair["Value"].GetValue<string>(), out var date))
                {
                    track.AdditionalFields["YEAR"] = date.Year.ToString();
                    track.Date = date;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Track number" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var trackNumber))
                {
                    track.TrackNumber = trackNumber;
                }
            }
            else if (pair["Key"].GetValue<string>() == "Track total" && !string.IsNullOrWhiteSpace(pair["Value"].GetValue<string>()))
            {
                if (int.TryParse(pair["Value"].GetValue<string>(), out var trackTotal))
                {
                    track.TrackTotal = trackTotal;
                }
            }
            else
            {
                track.AdditionalFields[pair["Key"].GetValue<string>()] = pair["Value"].GetValue<string>();
            }
        }

        return track.Save();
    }
}