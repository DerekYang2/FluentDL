using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using FluentDL.Models;
using QobuzApiSharp.Models.Content;
using TagLib;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FluentDL.Helpers
{
    public class MetadataObject
    {
        public string[]? AlbumArtists
        {
            get;
            set;
        }

        public string? AlbumName
        {
            get;
            set;
        }

        public string[]? Artists
        {
            get;
            set;
        }

        public string? Title
        {
            get;
            set;
        }

        public string[]? Genres
        {
            get;
            set;
        }

        public DateTime? ReleaseDate
        {
            get;
            set;
        }

        public string? Isrc
        {
            get;
            set;
        }

        public string? Upc
        {
            get;
            set;
        }

        public string? Url
        {
            get;
            set;
        }

        public int? TrackNumber
        {
            get;
            set;
        }

        public int? TrackTotal
        {
            get;
            set;
        }

        public string? AlbumArtPath
        {
            get;
            set;
        }

        public string? FilePath
        {
            get;
            internal set;
        }

        public string? Codec
        {
            get;
            internal set;
        }

        public int Duration
        {
            get;
            internal set;
        }

        public int AudioBitrate { get; internal set; }
        public int AudioChannels { get; internal set;  }
        public int AudioSampleRate { get; internal set; }
        public int BitsPerSample { get; internal set;  }

        public MetadataObject(string filePath)
        {
            FilePath = filePath;
            using TagLib.File tfile = TagLib.File.Create(filePath);
            tfile.Mode = TagLib.File.AccessMode.Closed;

            // Set codec
            foreach (var codec in tfile.Properties.Codecs)
            {
                if (codec.MediaTypes == TagLib.MediaTypes.Audio)
                {
                    Codec = codec.Description;
                    break;
                }
            }

            // Set duration
            Duration = (int)Math.Round(tfile.Properties.Duration.TotalSeconds);

            AlbumArtists = tfile.Tag.AlbumArtists;
            AlbumName = tfile.Tag.Album;
            Artists = tfile.Tag.Performers;
            Title = tfile.Tag.Title;
            Genres = tfile.Tag.Genres;
            Isrc = GetISRC(tfile);
            TrackNumber = Convert.ToInt32(tfile.Tag.Track);
            TrackTotal = Convert.ToInt32(tfile.Tag.TrackCount);
            if (tfile.Tag.Year != 0)
            {
                ReleaseDate = new DateTime(Convert.ToInt32(tfile.Tag.Year), 1, 1); // Default to January 1st if only year is provided
            }
            AudioBitrate = tfile.Properties.AudioBitrate;
            AudioChannels = tfile.Properties.AudioChannels;
            AudioSampleRate = tfile.Properties.AudioSampleRate;
            BitsPerSample = tfile.Properties.BitsPerSample;

            if (Path.GetExtension(tfile.Name) == ".flac" || Path.GetExtension(tfile.Name) == ".ogg") // Both .flac and .ogg files use the XiphComments
            {
                var custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

                // Release Date tag
                if (DateTime.TryParse(custom.GetFirstField("DATE"), out var date))
                {
                    ReleaseDate = date;
                }

                // UPC tag
                Upc = custom.GetFirstField("UPC");
                Url = custom.GetFirstField("URL");
            }
            else if (Path.GetExtension(tfile.Name) == ".mp3")
            {
                var custom = (TagLib.Id3v2.Tag)tfile.GetTag(TagTypes.Id3v2, true);
                // Release Date tag
                if (DateTime.TryParse(custom.GetTextAsString("TDRL"), out var date))
                {
                    ReleaseDate = date;
                }

                Url = custom.GetTextAsString("WOAS");
            }
        }

        // Inside MetadataObject
        public byte[]? GetAlbumArt()
        {
            using var tfile = TagLib.File.Create(FilePath);
            tfile.Mode = TagLib.File.AccessMode.Closed;

            IPicture[] pictures = tfile.Tag.Pictures;
            byte[]? albumArt = null;
            if (pictures.Length > 0)
            {
                // Get front cover
                albumArt = pictures[0].Data.Data; // Default to first picture
                foreach (var picture in pictures) // Find front cover if possible
                {
                    if (picture.Type == PictureType.FrontCover)
                    {
                        albumArt = picture.Data.Data;
                        break;
                    }
                }
            }
            return albumArt;
        }

        public async Task SaveAsync()
        {
            try 
            { 
                using var tfile = TagLib.File.Create(FilePath);
                tfile.Mode = TagLib.File.AccessMode.Write;
                tfile.Tag.AlbumArtists = AlbumArtists;
                tfile.Tag.Album = AlbumName;
                tfile.Tag.Performers = Artists;
                tfile.Tag.Title = Title;
                tfile.Tag.Genres = Genres;
                tfile.Tag.ISRC = Isrc;
                tfile.Tag.Track = Convert.ToUInt32(TrackNumber);
                tfile.Tag.TrackCount = Convert.ToUInt32(TrackTotal);

                // CUSTOM TAGS: https://wiki.hydrogenaud.io/index.php?title=Tag_Mapping

                if (Path.GetExtension(tfile.Name) == ".flac" || Path.GetExtension(tfile.Name) == ".ogg")
                {
                    var custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

                    // Override TRACKNUMBER tag again to prevent using "two-digit zero-filled value"
                    // See https://github.com/mono/taglib-sharp/pull/240 where this change was introduced in taglib-sharp v2.3
                    custom.SetField("TRACKNUMBER", Convert.ToUInt32(TrackNumber));

                    // Set Date fields
                    var releaseDateString = ReleaseDate?.ToString("yyyy-MM-dd") ?? "";
                    if (!string.IsNullOrWhiteSpace(releaseDateString))
                    {
                        // Release Year tag (The "tfile.Tag.Year" field actually writes to the DATE tag, so use custom tag)
                        custom.SetField("YEAR", releaseDateString.Substring(0, 4));

                        // Release Date tag
                        custom.SetField("DATE", releaseDateString);
                    }

                    // UPC tag
                    custom.SetField("UPC", Upc);
                    custom.SetField("URL", Url);
                }
                else if (Path.GetExtension(tfile.Name) == ".mp3")
                {
                    var custom = (TagLib.Id3v2.Tag)tfile.GetTag(TagTypes.Id3v2, true);

                    // Set single year
                    tfile.Tag.Year = Convert.ToUInt32(ReleaseDate?.Year ?? 0);

                    // Set the full release date
                    var releaseDateString = ReleaseDate?.ToString("yyyy-MM-dd") ?? "";
                    if (!string.IsNullOrWhiteSpace(releaseDateString))
                    {
                        custom.SetTextFrame("TDRL", releaseDateString);
                    }

                    custom.SetNumberFrame("TRCK", Convert.ToUInt32(TrackNumber), tfile.Tag.TrackCount);

                    // SRC URL
                    custom.SetTextFrame("WOAS", Url);
                }
                else
                {
                    tfile.Tag.Year = Convert.ToUInt32(ReleaseDate?.Year ?? 0);
                }

                byte[]? albumArt = null;
                if (!string.IsNullOrWhiteSpace(AlbumArtPath))
                {
                    if (System.IO.File.Exists(AlbumArtPath)) // Local
                    {
                        albumArt = await System.IO.File.ReadAllBytesAsync(AlbumArtPath); // Covert image to byte array
                    }
                    else
                    {
                        albumArt = await new HttpClient().GetByteArrayAsync(AlbumArtPath); // Download image
                    }
                }

                if (albumArt != null) // Save cover art if it exists
                {
                    if (Path.GetExtension(tfile.Name) == ".ogg")
                    {
                        var custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

                        TagLib.Flac.Picture pic = new TagLib.Flac.Picture(new Picture(albumArt)) { MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg, Type = PictureType.FrontCover };
                        TagLib.ByteVector picData = pic.Render();


                        custom.SetField("METADATA_BLOCK_PICTURE", Convert.ToBase64String(picData.Data));
                    }
                    else
                    {
                        TagLib.Id3v2.AttachmentFrame pic = new TagLib.Id3v2.AttachmentFrame { TextEncoding = StringType.Latin1, MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg, Type = PictureType.FrontCover, Data = albumArt };
                        // Save cover art to FLAC file.
                        tfile.Tag.Pictures = new IPicture[] { pic };
                    }
                }


                await Task.Run(tfile.Save);
            }
            catch (Exception e)
            {
                Debug.WriteLine("FAILED TO SAVE METADATA: " + e.Message);
            }
        }

        private static string? GetISRC(TagLib.File track)
        {
            if (track.Tag.ISRC != null)
            {
                return track.Tag.ISRC;
            }

            // Attempt to get from filename
            var fileName = track.Name;
            if (fileName != null)
            {
                var isrc = Regex.Match(fileName.Replace("-", ""), @"[A-Z]{2}[A-Z0-9]{3}\d{2}\d{5}").Value;
                if (isrc.Length == 12)
                {
                    return isrc;
                }
            }

            return null;
        }

        public ObservableCollection<MetadataPair> GetMetadataPairCollection()
        {
            return new ObservableCollection<MetadataPair>
            {
                new() { Key = "Title", Value = Title },
                new() { Key = "Contributing artists", Value = string.Join(";", Artists ?? Array.Empty<string>()) },
                new() { Key = "Genre", Value = string.Join(";", Genres ?? Array.Empty<string>()) },
                new() { Key = "Album", Value = AlbumName },
                new() { Key = "Album artist", Value = string.Join(";", AlbumArtists ?? Array.Empty<string>()) },
                new() { Key = "ISRC", Value = Isrc },
                new() { Key = "Date", Value = ReleaseDate?.ToString("yyyy-MM-dd") },
                new() { Key = "Track number", Value = TrackNumber.ToString() },
                new() { Key = "Track total", Value = TrackTotal.ToString() }
            };
        }

        public void SetFields(ObservableCollection<MetadataPair> collection)
        {
            foreach (var pair in collection)
            {
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;

                switch (pair.Key)
                {
                    case "Title":
                        Title = pair.Value;
                        break;
                    case "Contributing artists":
                        // split and trim each item
                        Artists = (pair.Value.Contains(";") ? pair.Value.Split(";") : pair.Value.Split(",")).Select(x => x.Trim()).ToArray();
                        break;
                    case "Genre":
                        Genres = (pair.Value.Contains(";") ? pair.Value.Split(";") : pair.Value.Split(",")).Select(x => x.Trim()).ToArray();
                        break;
                    case "Album":
                        AlbumName = pair.Value;
                        break;
                    case "Album artist":
                        AlbumArtists = (pair.Value.Contains(";") ? pair.Value.Split(";") : pair.Value.Split(",")).Select(x => x.Trim()).ToArray();
                        break;
                    case "ISRC":
                        Isrc = pair.Value;
                        break;
                    case "Date":
                        if (pair.Value.Length == 4 && int.TryParse(pair.Value, out var year))
                        {
                            ReleaseDate = new DateTime(year, 1, 1); // Assume January 1st if only year is provided
                        }
                        else if (DateTime.TryParse(pair.Value, out var date))
                        {
                            ReleaseDate = date;
                        }

                        break;
                    case "Track number":
                        TrackNumber = Convert.ToInt32(pair.Value);
                        break;
                    case "Track total":
                        TrackTotal = Convert.ToInt32(pair.Value);
                        break;
                }
            }
        }
    }
}