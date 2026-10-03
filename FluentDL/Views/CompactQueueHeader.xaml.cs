using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactQueueHeader : UserControl
{
    public CompactQueueHeader()
    {
        InitializeComponent();
    }

    public CompactQueueColumns Columns => CompactQueueColumns.Shared;

    private void CompactQueueHeader_SizeChanged(object sender, SizeChangedEventArgs e) => Columns.Update(e.NewSize.Width);
}
