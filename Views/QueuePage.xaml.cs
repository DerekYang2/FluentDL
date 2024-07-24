using System.Collections.ObjectModel;
using System.Diagnostics;
using ABI.Windows.UI.ApplicationSettings;
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
        cancellationTokenSource = new CancellationTokenSource();
        InitPreviewPanelButtons();
        StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button initially

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (QueueViewModel.Source.Count == 0)
                {
                    ProgressText.Text = "No tracks in queue";
                    QueueProgress.Value = 0;
                    StartStopButton.Visibility = Visibility.Collapsed;
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

                // Check if pause was called
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    SetContinueUI();
                }
            });
        };
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
        if (string.IsNullOrWhiteSpace(CommandInput.Text) || QueueViewModel.IsRunning || QueueViewModel.Source.Count == 0) // If the command is empty or the queue is currently running, return
        {
            return;
        }

        StartStopButton.Visibility = Visibility.Visible; // Display start stop
        QueueViewModel.SetCommand(CommandInput.Text);
        QueueViewModel.Reset();
        cancellationTokenSource = new CancellationTokenSource();
        QueueViewModel.RunCommand(DirectoryInput.Text, cancellationTokenSource.Token);
        SetPauseUI();
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        QueueViewModel.Clear();
        StartStopButton.Visibility = Visibility.Collapsed; // Display start stop
    }

    private void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueViewModel.IsRunning) // If the queue is running, cancel 
        {
            cancellationTokenSource.Cancel();
            StartStopText.Text = "Pausing ... ";
        }
        else // If the queue is not running, start
        {
            cancellationTokenSource = new CancellationTokenSource();
            QueueViewModel.RunCommand(DirectoryInput.Text, cancellationTokenSource.Token);
            SetPauseUI();
        }
    }

    private void SetContinueUI()
    {
        StartStopIcon.Glyph = "\uE768"; // Change the icon to a start icon
        StartStopText.Text = "Continue";
        CommandButton.IsEnabled = true;
        ClearButton.IsEnabled = true;
    }

    private void SetPauseUI()
    {
        StartStopIcon.Glyph = "\uE769"; // Change the icon to a pause icon
        StartStopText.Text = "Pause";
        CommandButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
    }
}