using FluentDL.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactResultRow : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(SongSearchObject), typeof(CompactResultRow), new PropertyMetadata(null));
    public static readonly DependencyProperty OutputPathProperty = DependencyProperty.Register(
        nameof(OutputPath), typeof(string), typeof(CompactResultRow), new PropertyMetadata(null));
    public static readonly DependencyProperty OpenTagProperty = DependencyProperty.Register(
        nameof(OpenTag), typeof(object), typeof(CompactResultRow), new PropertyMetadata(null));
    public static readonly DependencyProperty FailureVisibilityProperty = DependencyProperty.Register(
        nameof(FailureVisibility), typeof(Visibility), typeof(CompactResultRow), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty CachedArtworkOnlyProperty = DependencyProperty.Register(
        nameof(CachedArtworkOnly), typeof(bool), typeof(CompactResultRow), new PropertyMetadata(false));

    public CompactResultRow() => InitializeComponent();
    public SongSearchObject? Item { get => (SongSearchObject?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public string? OutputPath { get => (string?)GetValue(OutputPathProperty); set => SetValue(OutputPathProperty, value); }
    public object? OpenTag { get => GetValue(OpenTagProperty); set => SetValue(OpenTagProperty, value); }
    public Visibility FailureVisibility { get => (Visibility)GetValue(FailureVisibilityProperty); set => SetValue(FailureVisibilityProperty, value); }
    public bool CachedArtworkOnly { get => (bool)GetValue(CachedArtworkOnlyProperty); set => SetValue(CachedArtworkOnlyProperty, value); }
    public event RoutedEventHandler? OpenClick;
    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenClick?.Invoke(sender, e);
    public static string TrackCount(SongSearchObject? item) => item is AlbumSearchObject album
        ? $"{album.TracksCount} {(album.TracksCount == 1 ? "track" : "tracks")}" : string.Empty;
    public static Visibility OpenVisibility(string? path) => File.Exists(path) ? Visibility.Visible : Visibility.Collapsed;
}
