using Microsoft.UI.Xaml.Media.Imaging;

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

    public string Isrc
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

    public BitmapImage? LocalBitmapImage
    {
        get;
        set;
    }

    public SongSearchObject()
    {
    }

    public override string ToString()
    {
        return Source + " | Title: " + Title + ", Artists: " + Artists + ", Duration: " + Duration + ", Rank: " + Rank + ", Release Date: " + ReleaseDate + ", Image Location: " + ImageLocation + ", Id: " + Id + ", Album Name: " + AlbumName;
    }
}