
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;

namespace FluentDL.Models;

public class SongSearchObject
{
    public string Title
    {
        get;
        set;
    }

    public string? ImageLocation
    {
        get;
        set;
    }

    public string? Isrc
    {
        get;
        set;
    }

    public string Id
    {
        get;
        set;
    }

    public string ReleaseDate
    {
        get;
        set;
    }

    public string Artists
    {
        get;
        set;
    }

    public string Duration
    {
        get;
        set;
    }

    public string Rank
    {
        get;
        set;
    }

    public string AlbumName
    {
        get;
        set;
    }

    public string Source
    {
        get;
        set;
    }

    public bool Explicit
    {
        get;
        set;
    }

    public string TrackPosition
    {
        get;
        set;
    }

    [JsonIgnore]
    public BitmapImage? LocalBitmapImage
    {
        get;
        set;
    }

    public string? AudioFormat
    {
        get;
        set;
    }

    public Dictionary<string, object?>? AdditionalFields { get; set; } = null;

    public SongSearchObject()
    {
    }

    public override string ToString()
    {
        var settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Error = (sender, args) => args.ErrorContext.Handled = true
        };
        return JsonConvert.SerializeObject(this, settings);
    }

    public override int GetHashCode()
    {
        return (Source + Id).GetHashCode();
    }
}