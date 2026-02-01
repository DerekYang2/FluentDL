
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentDL.Models;

public class SongSearchObject : INotifyPropertyChanged
{
    public string Title
    {
        get;
        set;
    } = string.Empty;

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
    } = string.Empty;

    public string Artists
    {
        get;
        set;
    } = string.Empty;

    public string Duration
    {
        get;
        set;
    } = "0";

    public string Rank
    {
        get;
        set;
    } = string.Empty;

    public string AlbumName
    {
        get;
        set;
    } = string.Empty;

    public string Source
    {
        get;
        set;
    }

    public bool Explicit
    {
        get;
        set;
    } = false;

    public string TrackPosition
    {
        get;
        set;
    } = "1";

    // For ordering purposes
    public int? QueueCounter
    {
        get; set;
    }

    [JsonIgnore]
    private BitmapImage? _localBitmapImage;
    [JsonIgnore]
    public BitmapImage? LocalBitmapImage
    {
        get => _localBitmapImage;
        set
        {
            if (_localBitmapImage != value)
            {
                _localBitmapImage = value;
                OnPropertyChanged();
            }
        }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}