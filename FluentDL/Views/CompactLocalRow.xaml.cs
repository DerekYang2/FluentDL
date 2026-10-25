using FluentDL.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactLocalRow : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(SongSearchObject), typeof(CompactLocalRow), new PropertyMetadata(null));
    public CompactLocalRow() => InitializeComponent();
    public SongSearchObject? Item { get => (SongSearchObject?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public CompactLocalColumns Columns => CompactLocalColumns.Shared;
}
