using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace FluentDL.Views;

// One set of column widths for the compact queue header and every compact row, so they stay aligned.
// The header reports its width, and secondary columns hide as the queue narrows.
public sealed partial class CompactQueueColumns : ObservableObject
{
    private static readonly GridLength Hidden = new(0);

    public static CompactQueueColumns Shared { get; } = new();

    public GridLength ArtworkWidth { get; } = new(56);
    public GridLength TitleWidth { get; } = new(2, GridUnitType.Star);
    public GridLength DurationWidth { get; } = new(80);
    public GridLength ActionWidth { get; } = new(32);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumWidth), nameof(AlbumVisibility))]
    private bool _showAlbum = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YearWidth), nameof(YearVisibility))]
    private bool _showYear = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceWidth), nameof(SourceVisibility))]
    private bool _showSource = true;

    public GridLength AlbumWidth => ShowAlbum ? new GridLength(1, GridUnitType.Star) : Hidden;
    public Visibility AlbumVisibility => ShowAlbum ? Visibility.Visible : Visibility.Collapsed;

    public GridLength YearWidth => ShowYear ? new GridLength(64) : Hidden;
    public Visibility YearVisibility => ShowYear ? Visibility.Visible : Visibility.Collapsed;

    public GridLength SourceWidth => ShowSource ? new GridLength(86) : Hidden;
    public Visibility SourceVisibility => ShowSource ? Visibility.Visible : Visibility.Collapsed;

    private CompactQueueColumns()
    {
    }

    public void Update(double width)
    {
        ShowSource = width >= 480;
        ShowAlbum = width >= 720;
        ShowYear = width >= 920;
    }
}
