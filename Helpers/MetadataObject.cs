using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using FluentDL.Models;
using QobuzApiSharp.Models.Content;
using TagLib;

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

        public string[]? Genre
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

        public byte[]? AlbumArt
        {
            get;
            set;
        }

        public string? AlbumArtPath
        {
            get;
            set;
        }

        private string? FilePath
        {
            get;
            set;
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

        public TagLib.File tfile;

        public MetadataObject(string filePath)
        {
            FilePath = filePath;
            tfile = TagLib.File.Create(filePath);
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

            bool isFlac = Codec.ToLower().Contains("flac") || tfile.Name.EndsWith(".flac");

            AlbumArtists = tfile.Tag.AlbumArtists;
            AlbumName = tfile.Tag.Album;
            Artists = tfile.Tag.Performers;
            Title = tfile.Tag.Title;
            Genre = tfile.Tag.Genres;
            Isrc = GetISRC(tfile);
            TrackNumber = Convert.ToInt32(tfile.Tag.Track);
            TrackTotal = Convert.ToInt32(tfile.Tag.TrackCount);
            if (tfile.Tag.Year != 0)
            {
                ReleaseDate = new DateTime(Convert.ToInt32(tfile.Tag.Year), 1, 1); // Default to January 1st if only year is provided
            }

            TagLib.Ogg.XiphComment custom;

            if (isFlac) // TODO: look into tags for other formats
            {
                custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

                // Release Date tag
                if (DateTime.TryParse(custom.GetFirstField("DATE"), out var date))
                {
                    ReleaseDate = date;
                }

                // UPC tag
                Upc = custom.GetFirstField("UPC");
                Url = custom.GetFirstField("URL");
            }

            IPicture[] pictures = tfile.Tag.Pictures;
            if (pictures.Length > 0)
            {
                // Get front cover
                AlbumArt = pictures[0].Data.Data; // Default to first picture
                foreach (var picture in pictures) // Find front cover if possible
                {
                    if (picture.Type == PictureType.FrontCover)
                    {
                        AlbumArt = picture.Data.Data;
                        break;
                    }
                }
            }
        }

        public void Save()
        {
            tfile.Tag.AlbumArtists = AlbumArtists;
            tfile.Tag.Album = AlbumName;
            tfile.Tag.Performers = Artists;
            tfile.Tag.Title = Title;
            tfile.Tag.Genres = Genre;
            tfile.Tag.ISRC = Isrc;
            tfile.Tag.Track = Convert.ToUInt32(TrackNumber);
            tfile.Tag.TrackCount = Convert.ToUInt32(TrackTotal);

            TagLib.Ogg.XiphComment custom;

            if (tfile.Name.EndsWith(".flac"))
            {
                custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

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

            if (AlbumArt != null) // Save cover art if it exists
            {
                // Define cover art to use for FLAC file(s)
                TagLib.Id3v2.AttachmentFrame pic = new TagLib.Id3v2.AttachmentFrame { TextEncoding = TagLib.StringType.Latin1, MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg, Type = TagLib.PictureType.FrontCover, Data = new ByteVector(AlbumArt) };
                // Save cover art to FLAC file.
                tfile.Tag.Pictures = new TagLib.IPicture[1] { pic };
            }

            tfile.Save();
        }

        public async Task SaveAsync()
        {
            if (AlbumArt == null && !string.IsNullOrWhiteSpace(AlbumArtPath)) // Save cover art if it exists
            {
                AlbumArt = await new HttpClient().GetByteArrayAsync(AlbumArtPath); // Download image
            }

            await Task.Run(() => Save());
        }

        public byte[]? GetAlbumArt()
        {
            if (AlbumArt == null) return null;
            // Get a deep copy of the album art byte array
            byte[] deepCopy = new byte[AlbumArt.Length];
            Buffer.BlockCopy(AlbumArt, 0, deepCopy, 0, AlbumArt.Length);
            return deepCopy;
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
                var isrc = Regex.Match(fileName, @"[A-Z]{2}[A-Z0-9]{3}\d{2}\d{5}").Value;
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
                new() { Key = "Genre", Value = string.Join(";", Genre ?? Array.Empty<string>()) },
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
                        Genre = (pair.Value.Contains(";") ? pair.Value.Split(";") : pair.Value.Split(",")).Select(x => x.Trim()).ToArray();
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