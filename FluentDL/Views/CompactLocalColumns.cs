using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace FluentDL.Views;

public sealed class CompactLocalColumns : ObservableObject
{
    public static CompactLocalColumns Shared { get; } = new();
    private bool album = true;
    private bool year = true;
    public GridLength TitleWidth { get; } = new(2, GridUnitType.Star);
    public GridLength AlbumWidth => album ? new(1, GridUnitType.Star) : new(0);
    public GridLength YearWidth => new(year ? 64 : 0);
    public GridLength DurationWidth { get; } = new(80);
    public GridLength FormatWidth { get; } = new(88);
    public Visibility AlbumVisibility => album ? Visibility.Visible : Visibility.Collapsed;
    public Visibility YearVisibility => year ? Visibility.Visible : Visibility.Collapsed;

    public void Update(double width)
    {
        if (album != (width >= 720))
        {
            album = width >= 720;
            OnPropertyChanged(nameof(AlbumWidth));
            OnPropertyChanged(nameof(AlbumVisibility));
        }
        if (year != (width >= 920))
        {
            year = width >= 920;
            OnPropertyChanged(nameof(YearWidth));
            OnPropertyChanged(nameof(YearVisibility));
        }
    }
}
