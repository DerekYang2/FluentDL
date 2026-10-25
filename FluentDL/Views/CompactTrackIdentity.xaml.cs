using FluentDL.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactTrackIdentity : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(SongSearchObject), typeof(CompactTrackIdentity), new PropertyMetadata(null));
    public static readonly DependencyProperty CachedArtworkOnlyProperty = DependencyProperty.Register(
        nameof(CachedArtworkOnly), typeof(bool), typeof(CompactTrackIdentity), new PropertyMetadata(true));

    public CompactTrackIdentity() => InitializeComponent();
    public SongSearchObject? Item { get => (SongSearchObject?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool CachedArtworkOnly { get => (bool)GetValue(CachedArtworkOnlyProperty); set => SetValue(CachedArtworkOnlyProperty, value); }
    public static bool AllowUrlArtwork(bool cachedOnly) => !cachedOnly;
}
