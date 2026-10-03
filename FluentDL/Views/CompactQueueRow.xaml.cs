using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactQueueRow : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(QueueObject), typeof(CompactQueueRow), new PropertyMetadata(null));

    public CompactQueueRow()
    {
        InitializeComponent();
    }

    public QueueObject? Item
    {
        get => (QueueObject?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public CompactQueueColumns Columns => CompactQueueColumns.Shared;

    // Raised with the clicked button as the sender. The button's Tag holds the row's track.
    public event RoutedEventHandler? SpectrogramClick;

    private void SpectrogramButton_Click(object sender, RoutedEventArgs e) => SpectrogramClick?.Invoke(sender, e);
}
