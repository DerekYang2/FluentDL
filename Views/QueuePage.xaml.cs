using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.AppService;
using ABI.Windows.UI.ApplicationSettings;
using CommunityToolkit.WinUI.UI.Controls;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;

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
    private DispatcherQueue dispatcherQueue;
    private DispatcherTimer dispatcherTimer;
    private CancellationTokenSource cancellationTokenSource;

    public QueueViewModel ViewModel
    {
        get;
    }

    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();

        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;

        CustomListView.ItemsSource = QueueViewModel.Source;
        cancellationTokenSource = new CancellationTokenSource();
        InitPreviewPanelButtons();
        StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button initially

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            OnQueueSourceChange();
        };
    }

    private async void OutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Get the button that was clicked
        var button = sender as Button;

        if (button != null)
        {
            // Get the data context of the button (the item in the ListView)

            if (button.DataContext is QueueObject queueObject)
            {
                OutputDialog.XamlRoot = this.XamlRoot;
                OutputMessage.Text = $"Terminal output for track \"{queueObject.Title}\":";
                OutputTextBox.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, queueObject.ResultString);
                await OutputDialog.ShowAsync();
            }
        }
    }

    private void OnQueueSourceChange()
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            // Check if pause was called
            if (cancellationTokenSource.Token.IsCancellationRequested)
            {
                SetContinueUI(); // If paused, UI should show continue
                ShowInfoBar(InfoBarSeverity.Informational, "Queue paused");
            }

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

                if (completedCount == QueueViewModel.Source.Count) // If all tracks are completed
                {
                    StartStopButton.Visibility = Visibility.Collapsed; // Hide the start/stop button
                    // Enable other buttons
                    CommandButton.IsEnabled = true;
                    ClearButton.IsEnabled = true;
                    // Send notification
                    App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
                }
            }
        });
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

        // Set latest command used if null
        if (CommandInput.Text == null || CommandInput.Text.Trim().Length == 0)
        {
            CommandInput.Text = await LocalCommands.GetLatestCommand();
        }

        // Set latest path used if null
        if (DirectoryInput.Text == null || DirectoryInput.Text.Trim().Length == 0)
        {
            DirectoryInput.Text = await LocalCommands.GetLatestPath();
        }

        await CustomCommandDialog.ShowAsync();
    }

    private async void CustomCommandDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var commandInputText = CommandInput.Text.Trim();
        var directoryInputText = DirectoryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(commandInputText) || QueueViewModel.IsRunning || QueueViewModel.Source.Count == 0) // If the command is empty or the queue is currently running, return
        {
            return;
        }

        StartStopButton.Visibility = Visibility.Visible; // Display start stop
        QueueViewModel.SetCommand(commandInputText);
        QueueViewModel.Reset(); // Reset the queue object result strings and index
        cancellationTokenSource = new CancellationTokenSource();
        QueueViewModel.RunCommand(directoryInputText, cancellationTokenSource.Token);
        SetPauseUI();

        LocalCommands.AddCommand(commandInputText); // Add the command to the previous command list
        LocalCommands.AddPath(directoryInputText); // Add the path to the previous path list

        await LocalCommands.SaveLatestCommand(commandInputText);
        await LocalCommands.SaveLatestPath(directoryInputText);
        await LocalCommands.SaveCommands();
        await LocalCommands.SavePaths();
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        QueueViewModel.Clear();
        StartStopButton.Visibility = Visibility.Collapsed; // Display start stop
    }

    private void StartStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (StartStopText.Text.Equals("Pausing ... ")) // If already attempted a pause
        {
            return;
        }

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

    private void CommandInput_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var suitableItems = new List<string>();
            var inputLower = sender.Text.ToLower().Trim();

            foreach (var command in LocalCommands.GetCommandList())
            {
                if (suitableItems.Count >= 10) // Limit the number of suggestions to 10
                {
                    break;
                }

                if (command.ToLower().Trim().StartsWith(inputLower))
                {
                    suitableItems.Add(command);
                }
            }

            if (suitableItems.Count == 0)
            {
                suitableItems.Add("No command found");
            }

            sender.ItemsSource = suitableItems;
        }
    }

    private void CommandInput_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = args.SelectedItem.ToString(); // Set the text to the chosen suggestion
    }

    private void DirectoryInput_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var suitableItems = new List<string>();
            var inputLower = sender.Text.ToLower();
            // Remove everything except letters and numbers
            inputLower = Regex.Replace(inputLower, "[^a-zA-Z0-9]", "");

            foreach (var command in LocalCommands.GetPathList())
            {
                if (suitableItems.Count >= 10) // Limit the number of suggestions to 10
                {
                    break;
                }

                if (Regex.Replace(command.ToLower(), "[^a-zA-Z0-9]", "").Contains(inputLower)) // Compare only alphanumeric characters
                {
                    suitableItems.Add(command);
                }
            }

            if (suitableItems.Count == 0)
            {
                suitableItems.Add("No directory found");
            }

            sender.ItemsSource = suitableItems;
        }
    }

    private void DirectoryInput_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = args.SelectedItem.ToString(); // Set the text to the chosen suggestion
    }

    // Required for animation to work
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            PageInfoBar.Content = message;
        });
        dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
        dispatcherTimer.Start();
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object sender, object e)
    {
        PageInfoBar.Opacity = 0;
        (sender as DispatcherTimer).Stop();
        // Set IsOpen to false after 0.25 seconds
        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            dispatcherQueue.TryEnqueue(() =>
            {
                PageInfoBar.IsOpen = false;
            });
        });
    }

    private void OutputDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string text;
        OutputTextBox.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out text); // Get the text from the RichEditBox
        // Copy the text to the clipboard
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        ShowInfoBar(InfoBarSeverity.Success, "Copied to clipboard");
    }
}