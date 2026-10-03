using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace FluentDL.ViewModels;

// Shortcut buttons shown on every row of the standard queue layout. Rows bind to this one instance,
// so changing a shortcut setting updates existing rows without touching each queued item.
public sealed partial class QueueShortcuts : ObservableObject
{
    public static QueueShortcuts Shared { get; } = new();

    [ObservableProperty]
    private Visibility _shareVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _downloadCoverVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _removeVisibility = Visibility.Collapsed;

    private QueueShortcuts()
    {
    }
}
