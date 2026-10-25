using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CompactLocalHeader : UserControl
{
    public CompactLocalHeader() => InitializeComponent();
    public CompactLocalColumns Columns => CompactLocalColumns.Shared;
    private void Header_SizeChanged(object sender, SizeChangedEventArgs e) => Columns.Update(e.NewSize.Width);
}
