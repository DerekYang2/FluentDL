using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.WinUI.UI.Controls;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace FluentDL.Views;

// Converter converts integer (queue list count) to a message
public class QueueMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value + " tracks in queue" + (QueuePage.IsLoading ? " (loading...)" : "");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class PathToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed partial class QueuePage : Page
{
    // Create dispatcher queue
    private DispatcherQueue _dispatcherQueue;

    public static bool IsLoading
    {
        get;
        set;
    }

    public QueueViewModel ViewModel
    {
        get;
    }

    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        CustomListView.ItemsSource = QueueViewModel.Source;
        InitPreviewPanelButtons();
        IsLoading = false;
    }

    private void InitPreviewPanelButtons()
    {
        var downloadButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Download), Label = "Download" };

        var downloadCoverButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uEE71" }, Label = "Download Cover" };

        var removeButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Delete), Label = "Remove" };
        removeButton.Click += async (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
            if (selectedSong == null)
            {
                return;
            }

            QueueViewModel.Remove(selectedSong);
            PreviewPanel.Clear();
        };

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { downloadButton, downloadCoverButton, removeButton });
    }

    private async void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            PreviewPanel.Clear();
            return;
        }

        PreviewPanel.Show();
        await PreviewPanel.Update(selectedSong);
    }
}