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
        if ((int)value == 0)
        {
            return "No tracks in queue";
        }

        return value + " tracks in queue";
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
    private CancellationTokenSource cancellationTokenSource;

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

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (QueueViewModel.Source.Count == 0)
                {
                    ProgressText.Text = "No tracks in queue";
                }
                else
                {
                    var completedCount = QueueViewModel.GetCompletedCount();
                    QueueProgress.Value = 100.0 * completedCount / QueueViewModel.Source.Count;
                    if (completedCount > 0)
                    {
                        ProgressText.Text = (QueueViewModel.IsRunning ? "Running " : "Completed ") + $"{QueueViewModel.GetCompletedCount()} of {QueueViewModel.Source.Count}";
                    }
                }
            });
        };

        InitPreviewPanelButtons();
        cancellationTokenSource = new CancellationTokenSource();
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

    private async void CommandButton_OnClick(object sender, RoutedEventArgs e)
    {
        CustomCommandDialog.XamlRoot = this.XamlRoot;
        await CustomCommandDialog.ShowAsync();
    }

    private void CustomCommandDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(CommandInput.Text))
        {
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        QueueViewModel.RunCommand(CommandInput.Text, cancellationTokenSource.Token);
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        QueueViewModel.Clear();
    }
}