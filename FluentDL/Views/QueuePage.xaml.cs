using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
using FluentDL.Core.Helpers;
using FluentDL.Core.Sources;
using FluentDL.Contracts.Sources;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using FluentDL.Contracts.Services;
using FluentDL.Core.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using FileSavePicker = Windows.Storage.Pickers.FileSavePicker;
using Symbol = Microsoft.UI.Xaml.Controls.Symbol;
namespace FluentDL.Views;

public partial class PathToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed partial class QueuePage : Page
{
    private sealed record BackupOption(string DisplayName, FileInfo? File);
    private const string BackupFileSuffix = ".zip";
    private readonly DataTemplate _regularQueueItemTemplate;
    private readonly Style? _regularQueueItemContainerStyle;
    public delegate void QueueRunCallback(InfoBarSeverity severity, string message);

    // Create callback
    private QueueRunCallback queueRunCallback;

    // Create dispatcher queue
    private DispatcherQueue dispatcherQueue;
    private DispatcherTimer dispatcherTimer;
    private CancellationTokenSource cancellationTokenSource;
    private readonly ConversionService conversions = App.GetService<ConversionService>();
    private readonly DownloadService downloads = App.GetService<DownloadService>();
    private bool playlistDialogOpen;

    // Conversion variables
    private ConcurrentDictionary<SongSearchObject, int> successSource, warningSource, errorSource;

    // One checkbox per source in the clear and conversion dialogs, in catalog order.
    public IReadOnlyList<SourceOption> ClearOptions { get; } = SourceCatalog.All.Select(s => new SourceOption(s) { IsChecked = true }).ToList();
    public IReadOnlyList<SourceOption> ConversionOptions { get; } = SourceCatalog.All.Select(s => new SourceOption(s) { IsChecked = false }).ToList();

    private static readonly object _lock = new object();
    private static bool _isConverting = false;

    public static bool IsConverting
    {
        get
        {
            lock (_lock)
            {
                return _isConverting;
            }
        }
        set
        {
            lock (_lock)
            {
                _isConverting = value;
            }
        }
    }

    public QueueViewModel ViewModel
    {
        get;
    }

    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();
        _regularQueueItemTemplate = CustomListView.ItemTemplate;
        _regularQueueItemContainerStyle = CustomListView.ItemContainerStyle;
        // The page is cached and the preference loads at startup, so one subscription keeps the layout current.
        ViewModel.QueueDisplay.PropertyChanged += QueueDisplay_PropertyChanged;
        ApplyQueueLayout();
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += DispatcherTimer_Tick;

        CustomListView.ItemsSource = QueueViewModel.Source;
        cancellationTokenSource = new CancellationTokenSource();
        InitPreviewPanelButtons();
        InitializeAnimations();
        OutputComboBox.ItemsSource = SourceCatalog.All.Select(s => s.DisplayName).ToList();

        foreach (var option in ClearOptions)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SourceOption.IsChecked))
                {
                    UpdateClearSourcesAllState();
                }
            };
        }

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            OnQueueSourceChange();
        };

        NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = QueueViewModel.Source.Count > 0;

        // Set conversion variables
        successSource = new ConcurrentDictionary<SongSearchObject, int>();
        warningSource = new ConcurrentDictionary<SongSearchObject, int>();
        errorSource = new ConcurrentDictionary<SongSearchObject, int>();

        // Create callback
        queueRunCallback = (severity, message) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                ShowInfoBar(severity, message);
                SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications).ContinueWith((task) =>
                {
                    var showNotif = task.Result ?? false;

                    if (showNotif)
                    {
                        App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
                    }
                });
            });
        };
    }

    private void InitializeAnimations()
    {
        AnimationHelper.AttachSpringDownAnimation(DownloadAllButton, DownloadAllButtonIcon);
        AnimationHelper.AttachSpringRightAnimation(ConvertDialogOpenButton, ConvertDialogOpenIcon);
        AnimationHelper.AttachScaleAnimation(ClearButton, ClearIcon);
        AnimationHelper.AttachScaleAnimation(RestoreQueueButton, RestoreQueueIcon);
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e)
    {
        OnQueueSourceChange();

        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong != null)
        {
            PreviewPanel.Show();
            if (selectedSong.Source == SourceIds.Local)
            {
                await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
            }
            else
            {
                await PreviewPanel.Update(QueueObject.Unwrap(selectedSong));
            }
        }
        
        await QueueViewModel.UpdateShortcutVisibility(); // Initialize the settings for shortcut buttons
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Clear preview 
        PreviewPanel.Clear();
    }

    private void QueueDisplay_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueueDisplaySettings.IsCompact))
        {
            ApplyQueueLayout();
        }
    }

    private void ApplyQueueLayout()
    {
        var template = ViewModel.QueueDisplay.IsCompact
            ? (DataTemplate)Resources["CompactQueueItemTemplate"]
            : _regularQueueItemTemplate;
        if (CustomListView.ItemTemplate != template)
        {
            var firstVisibleIndex = (CustomListView.ItemsPanelRoot as ItemsStackPanel)?.FirstVisibleIndex ?? -1;
            var anchor = firstVisibleIndex > 0 && firstVisibleIndex < CustomListView.Items.Count
                ? CustomListView.Items[firstVisibleIndex]
                : null;
            CustomListView.ItemContainerStyle = ViewModel.QueueDisplay.IsCompact
                ? (Style)Resources["CompactQueueItemStyle"]
                : _regularQueueItemContainerStyle;
            CustomListView.ItemTemplate = template;
            if (anchor is not null)
            {
                CustomListView.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
            }
        }
    }

    private void CustomListView_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!ViewModel.QueueDisplay.IsCompact) return;
        // Mouse requests start inside the row and keyboard requests start on the focused item,
        // so both resolve to the row that asked rather than the selected one.
        var container = FindQueueItemContainer(e.OriginalSource as DependencyObject);
        if (container is null || CustomListView.ItemFromContainer(container) is not QueueObject song) return;

        foreach (var item in CompactQueueRowMenu.Items)
        {
            item.Tag = song;
        }
        var localVisibility = song.Source == SourceIds.Local ? Visibility.Visible : Visibility.Collapsed;
        CompactQueueLocalSeparator.Visibility = localVisibility;
        CompactQueueOpenSpekItem.Visibility = localVisibility;
        CompactQueueOpenLocalItem.Visibility = localVisibility;
        var outputVisibility = song.ResultString is null ? Visibility.Collapsed : Visibility.Visible;
        CompactQueueOutputSeparator.Visibility = outputVisibility;
        CompactQueueOutputItem.Visibility = outputVisibility;

        if (e.TryGetPosition(container, out var position))
        {
            CompactQueueRowMenu.ShowAt(container, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
        }
        else
        {
            CompactQueueRowMenu.ShowAt(container);
        }
        e.Handled = true;
    }

    private ListViewItem? FindQueueItemContainer(DependencyObject? element)
    {
        while (element is not null && element != CustomListView)
        {
            if (element is ListViewItem container) return container;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private async void CompactQueueButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.QueueDisplay.SetCompactAsync(CompactQueueButton.IsChecked == true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save the queue display preference");
            CompactQueueButton.IsChecked = ViewModel.QueueDisplay.IsCompact;
            ShowInfoBar(InfoBarSeverity.Error, "Could not save the queue display preference. The previous layout is still active.", 5);
        }
    }

    private async void OutputButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QueueObject queueObject })
        {
            OutputDialog.XamlRoot = this.XamlRoot;
            OutputMessage.Text = $"Terminal output for track \"{queueObject.Title}\":";
            OutputTextBox.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, queueObject.ResultString);
            await OutputDialog.ShowAsync();
        }
    }
    
    private void SetCountText()
    {
        var albums = QueueViewModel.Source.Count(q => q.IsAlbum);
        var tracks = QueueViewModel.Source.Count - albums;
        CountText.Text = FormatQueueCount(tracks, albums);
    }

    internal static string FormatQueueCount(int tracks, int albums)
    {
        if (tracks == 0 && albums == 0) return "No tracks";
        var parts = new List<string>();
        if (tracks > 0) parts.Add(tracks == 1 ? "1 track" : $"{tracks} tracks");
        if (albums > 0) parts.Add(albums == 1 ? "1 album" : $"{albums} albums");
        return string.Join(", ", parts);
    }

    private void OnQueueSourceChange()
    {
        SetCountText();
        NoItemsText.Visibility = QueueViewModel.Source.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = QueueViewModel.Source.Count > 0;
        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdateQueueToolbarLayout);
    }

    private void QueueToolbar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateQueueToolbarLayout();
    private bool updatingQueueToolbar;
    private readonly Dictionary<Button, Thickness> queueActionPadding = [];
    private readonly Dictionary<Button, (string Text, double FontSize, double Width)> queueLabelWidths = [];

    private void UpdateQueueToolbarLayout()
    {
        if (QueueToolbarInfo == null || QueueToolbarActions == null || QueueToolbar.ActualWidth <= 0) return;
        if (updatingQueueToolbar) return;
        updatingQueueToolbar = true;
        try
        {
            var countWidth = Math.Max(0, QueueToolbar.ActualWidth - CompactQueueButton.ActualWidth - 8);
            CountTextButton.MaxWidth = countWidth;
            ProgressTextButton.MaxWidth = countWidth;
            var actions = QueueToolbarActions.Children.OfType<Button>().Where(child => child.Visibility == Visibility.Visible).ToList();
            var fullWidth = 0d;
            foreach (var button in actions)
            {
                if (!queueActionPadding.ContainsKey(button)) queueActionPadding.Add(button, button.Padding);
                if (button.Content is not StackPanel panel || panel.Children.OfType<TextBlock>().FirstOrDefault() is not { } label) continue;
                if (!queueLabelWidths.TryGetValue(button, out var measured) || measured.Text != label.Text || measured.FontSize != label.FontSize)
                {
                    var measure = new TextBlock { Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily, FontWeight = label.FontWeight };
                    measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    measured = (label.Text, label.FontSize, measure.DesiredSize.Width);
                    queueLabelWidths[button] = measured;
                }
                var padding = queueActionPadding[button];
                fullWidth += Math.Max(32, padding.Left + padding.Right + button.BorderThickness.Left + button.BorderThickness.Right + 16 + 8 + measured.Width);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, button.Name);
                if (string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)))
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label.Text);
            }
            fullWidth += Math.Max(0, actions.Count - 1) * 8;
            var iconOnly = QueueToolbar.ActualWidth < fullWidth;
            foreach (var button in actions)
            {
                if (button.Content is not StackPanel panel) continue;
                foreach (var label in panel.Children.OfType<TextBlock>()) label.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
                foreach (var icon in panel.Children.OfType<FontIcon>()) icon.Margin = new Thickness(0);
                panel.Spacing = iconOnly ? 0 : 8;
                button.Padding = iconOnly ? new Thickness(8, 4, 8, 4) : queueActionPadding[button];
                button.MinWidth = 32;
            }
            var actionWidth = iconOnly ? actions.Count * 32 + Math.Max(0, actions.Count - 1) * 8 : fullWidth;
            var stacked = QueueToolbar.ActualWidth < QueueToolbarInfo.DesiredSize.Width + actionWidth + 8;
            var row = stacked ? 1 : 0;
            if (Grid.GetRow(QueueToolbarActions) == row) return;
            // Keep icon-only actions together below the count if the first row still cannot fit them.
            Grid.SetRow(QueueToolbarActions, row);
            Grid.SetColumn(QueueToolbarActions, stacked ? 0 : 1);
            Grid.SetColumnSpan(QueueToolbarActions, stacked ? 2 : 1);
            Grid.SetColumnSpan(QueueToolbarInfo, stacked ? 2 : 1);
            QueueToolbarActions.Margin = new Thickness(0, stacked ? 8 : 0, 0, 0);
        }
        finally { updatingQueueToolbar = false; }
    }

    private async void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (playlistDialogOpen) return;
        if (IsConverting)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Wait for the queue task to finish before creating a playlist.");
            return;
        }
        playlistDialogOpen = true;
        try
        {
            var model = new PlaylistDialogViewModel(App.GetService<SourceRegistry>().All<IPlaylistWriter>(), QueueViewModel.Source.ToArray());
            var dialog = new CreatePlaylistDialog(model) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await dialog.ShowAsync();
        }
        finally { playlistDialogOpen = false; }
    }

    private void InitPreviewPanelButtons()
    {
        var shareLinkButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Link), Label = "Link" };
        shareLinkButton.Click += (sender, e) => CopySongLink(PreviewPanel.GetSong());

        var downloadCoverButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uEE71" }, Label = "Save Cover" };
        downloadCoverButton.Click += async (sender, e) => await DownloadSongCover(PreviewPanel.GetSong());

        var removeButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uECC9" }, Label = "Remove" };
        removeButton.Click += async (sender, e) => await RemoveSongFromQueue(PreviewPanel.GetSong());

        AnimationHelper.AttachScaleAnimation(shareLinkButton);
        AnimationHelper.AttachSpringUpAnimation(downloadCoverButton);
        AnimationHelper.AttachScaleAnimation(removeButton);

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { downloadCoverButton, shareLinkButton, removeButton });
    }

    private void ShareLinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var song = button?.Tag as SongSearchObject;

        CopySongLink(song);
    }

    private async void DownloadCoverButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var song = button?.Tag as SongSearchObject;

        await DownloadSongCover(song);
    }

    private async void RemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var song = button?.Tag as SongSearchObject;

        await RemoveSongFromQueue(song);
    }

    private void OpenLocalButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var song = button?.Tag as SongSearchObject;

        if (song != null)
        {
            var argument = $"/select, \"{song.Id}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
    }

    private async void OpenSpekButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var song = button?.Tag as SongSearchObject;
        if (song == null || song.Source != SourceIds.Local || !File.Exists(song.Id))  // SongSearchObject.id for local tracks is the file path
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Track does not exist locally", 3);
            return;
        }
        await SpectrogramDialog.OpenSpectrogramDialog(song, this.XamlRoot);
    }

    private void CopySongLink(SongSearchObject? song)
    {
        if (song == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to copy source to clipboard");
            return;
        }

        var uri = ApiHelper.GetUrl(QueueObject.Unwrap(song));

        Clipboard.CopyToClipboard(uri);
        ShowInfoBar(InfoBarSeverity.Success, "Copied link to clipboard");
    }

    private async Task RemoveSongFromQueue(SongSearchObject? selectedSong)
    {
        if (selectedSong == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Error removing track from queue", 3);
            return;
        }

        if (IsConverting)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Cannot remove track while queue is running", 3);
            return;
        }

        await QueueViewModel.Remove(selectedSong);
        PreviewPanel.Clear();
    }

    private async Task DownloadSongCover(SongSearchObject? songObj)
    {
        // Check if the image is null
        if (songObj == null)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to download cover");
            return;
        }

        // Create file name
        var firstArtist = songObj.Artists.Split(",")[0].Trim();
        var isrcStr = !string.IsNullOrWhiteSpace(songObj.Isrc) ? $" [{songObj.Isrc}] " : "";
        var safeFileName = ApiHelper.GetSafeFilename(QueueObject.IsAlbumItem(songObj)
            ? $"{firstArtist} - {songObj.Title}{isrcStr}Cover Art.jpg" // an album has no track number; its code is a UPC
            : $"{songObj.TrackPosition}. {firstArtist} - {songObj.Title}{isrcStr}Cover Art.jpg");

        // Create a file save picker
        FileSavePicker savePicker = new() { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = safeFileName, };
        savePicker.FileTypeChoices.Add("Image", new List<string>() { ".jpg", ".png" });

        // Retrieve the window handle (HWND) of the current WinUI 3 window
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the file picker with the window handle (HWND)
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

        var file = await savePicker.PickSaveFileAsync();

        // Save bitmap image as jpg/png
        if (file != null)
        {
            byte[]? coverBytes;
            if (songObj.Source == SourceIds.Local)
            {
                coverBytes = await Task.Run(() => LocalExplorerViewModel.GetAlbumArtBytes(songObj.Id));
            }
            else
            {
                coverBytes = null;
                var coverUri = GetCoverUri(songObj);
                if (coverUri != null)
                {
                    try
                    {
                        coverBytes = await WebDownloads.GetBytesAsync(coverUri.AbsoluteUri);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Cover download failed");
                    }
                }
            }

            if (coverBytes == null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Failed to save cover", 5);
                return;
            }

            await File.WriteAllBytesAsync(file.Path, coverBytes);
            ShowInfoBar(InfoBarSeverity.Success, $"Cover saved to <a href='{file.Path}'>{file.Name}</a>", 5);
        }

    }

    // The preview loads a larger cover than the queue thumbnail, so use it when it shows this track.
    private Uri? GetCoverUri(SongSearchObject song)
    {
        if (PreviewPanel.GetSong() is { } shown && shown.Id == song.Id && shown.Source == song.Source
            && PreviewPanel.GetImage()?.UriSource is { } previewUri)
        {
            return previewUri;
        }

        return Uri.TryCreate(song.ImageLocation, UriKind.Absolute, out var location)
            && (location.Scheme == Uri.UriSchemeHttp || location.Scheme == Uri.UriSchemeHttps)
                ? location
                : null;
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
        if (selectedSong.Source == SourceIds.Local)
        {
            await PreviewPanel.Update(selectedSong, LocalExplorerViewModel.GetMetadataObject(selectedSong.Id));
        }
        else
        {
            await PreviewPanel.Update(QueueObject.Unwrap(selectedSong));
        }
    }

    private async void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueViewModel.Source.Count == 0)
        {
            return;
        }

        var selectedSources = await ConfirmClearQueueAsync();
        if (selectedSources == null || selectedSources.Count == 0)
        {
            return;
        }

        try
        {
            var autoBackupEnabled = await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.AutoBackupEnabled) ?? true;
            bool onlyLocal = selectedSources.Count == 1 && selectedSources.Contains(SourceIds.Local);
            int deletionCount = 0;
            foreach (var song in QueueViewModel.Source)
            {
                if (selectedSources.Contains(song.Source))
                {
                    deletionCount++;
                }
            }

            if (autoBackupEnabled && !onlyLocal && deletionCount > 0)  // Don't create backup if only clearing up local sources
            {
                var keep = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.BackupRetentionCount) ?? 5;
                await DatabaseService.CreateBackup(keep);
            }

            await QueueViewModel.ClearSources(selectedSources);
            ShowInfoBar(InfoBarSeverity.Informational, $"Cleared {deletionCount} items");
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, $"Failed to clear queue: {ex.Message}", 5);
        }
    }

    private async Task<HashSet<string>?> ConfirmClearQueueAsync()
    {
        ClearQueueDialog.XamlRoot = this.XamlRoot;
        InitializeClearSourcesSelection();

        var result = await ClearQueueDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var selectedSources = GetSelectedClearSources();
        return selectedSources.Count == 0 ? null : selectedSources;
    }

    private void InitializeClearSourcesSelection()
    {
        ClearSourcesAllCheckBox.IsChecked = true;
        foreach (var option in ClearOptions)
        {
            option.IsChecked = true;
        }
    }

    private HashSet<string> GetSelectedClearSources()
    {
        return ClearOptions.Where(option => option.IsChecked == true).Select(option => option.Id).ToHashSet();
    }

    private void ClearSourcesAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var states = ClearOptions.Select(option => option.IsChecked).ToList();
        if (states.Count == 0)
        {
            return;
        }

        if (states.All(state => state == false))
        {
            SetClearSourceSelections(true);
            ClearSourcesAllCheckBox.IsChecked = true;
        }
        else
        {
            SetClearSourceSelections(false);
            ClearSourcesAllCheckBox.IsChecked = false;
        }
    }

    private void SetClearSourceSelections(bool isChecked)
    {
        foreach (var option in ClearOptions)
        {
            option.IsChecked = isChecked;
        }
    }

    private void UpdateClearSourcesAllState()
    {
        var states = ClearOptions.Select(option => option.IsChecked).ToList();
        if (states.Count == 0)
        {
            ClearSourcesAllCheckBox.IsChecked = false;
            return;
        }

        if (states.All(state => state == true))
        {
            ClearSourcesAllCheckBox.IsChecked = true;
        }
        else if (states.All(state => state == false))
        {
            ClearSourcesAllCheckBox.IsChecked = false;
        }
        else
        {
            ClearSourcesAllCheckBox.IsChecked = null;
        }
    }

    private async void RestoreQueueButton_OnClick(object sender, RoutedEventArgs e)
    {
        var backups = DatabaseService.GetBackups();
        var backupOptions = BuildBackupOptions(backups, null);

        RestoreQueueDialog.XamlRoot = this.XamlRoot;
        RestoreQueueDialog.Title = "Restore queue";
        RestoreQueueDialog.CloseButtonText = "Cancel";
        RestoreQueueDialog.PrimaryButtonText = "Restore";
        RestoreQueueDialog.SecondaryButtonText = "Export";
        RestoreQueueDialog.DefaultButton = ContentDialogButton.Primary;
        RestoreQueueBackupComboBox.ItemsSource = backupOptions;
        RestoreQueueBackupComboBox.SelectedIndex = -1;
        UrlParser.ParseTextBlock(QueueBackupNoticeText,
            "Restore an automatic backup or import a user-created backup. Configure automatic backups at "
            + SettingsNavigation.Link(SettingsSection.Backups)
            + ".\nUse Browse to import a backup, or Export to save the current queue.", section =>
            {
                RestoreQueueDialog.Hide();
                SettingsNavigation.Open(section);
            });

        await RestoreQueueDialog.ShowAsync();
    }

    private async void RestoreQueueDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (RestoreQueueBackupComboBox.SelectedItem is not BackupOption { File: not null } option)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Select a backup to restore", 3);
            return;
        }

        try
        {
            await DatabaseService.RestoreZip(option.File.FullName);
            await QueueViewModel.LoadSaveQueue();
            ShowInfoBar(InfoBarSeverity.Success, "Queue restored from backup", 3);
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, $"Failed to restore backup: {ex.Message}", 5);
        }
    }

    private async void ImportBackupButton_OnClick(object sender, RoutedEventArgs e)
    {
        var file = await StoragePickerHelper.PickFileAsync([".zip"]);
        if (file == null)
        {
            return;
        }

        var fileInfo = new FileInfo(file.Path);
        if (!fileInfo.Exists)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Backup file not found", 3);
            return;
        }

        var backups = DatabaseService.GetBackups();
        var options = BuildBackupOptions(backups, fileInfo);
        RestoreQueueBackupComboBox.ItemsSource = options;
        RestoreQueueBackupComboBox.SelectedItem = options.FirstOrDefault(option =>
            option.File != null && option.File.FullName == fileInfo.FullName);
    }

    private async void RestoreQueueDialog_OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            var suggestedName = $"{DatabaseService.BackupFilePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}{BackupFileSuffix}";
            var file = await StoragePickerHelper.FileSavePickerAsync([".zip"], suggestedName);
            if (file == null)
            {
                return;
            }

            await DatabaseService.CreateBackupAtPath(file.Path);
            ShowInfoBar(InfoBarSeverity.Success, "Backup exported", 3);
            RefreshBackupOptions();
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, $"Failed to export backup: {ex.Message}", 5);
        }
    }

    private void RefreshBackupOptions()
    {
        var backups = DatabaseService.GetBackups();
        var options = BuildBackupOptions(backups, null);
        RestoreQueueBackupComboBox.ItemsSource = options;
        RestoreQueueBackupComboBox.SelectedIndex = -1;
    }

    private static List<BackupOption> BuildBackupOptions(List<FileInfo> backups, FileInfo? imported)
    {
        var options = backups
            .Select(backup => new BackupOption(GetBackupDisplayName(backup), backup))
            .ToList();

        if (imported != null && options.All(option => option.File?.FullName != imported.FullName))
        {
            options.Insert(0, new BackupOption(GetBackupDisplayName(imported), imported));
        }

        return options;
    }

    private static string GetBackupDisplayName(FileInfo backup)
    {
        var localTime = TryParseBackupTimestamp(backup.Name, out var parsed)
            ? parsed.ToLocalTime().ToString("g")
            : backup.LastWriteTime.ToString("g");
        return $"{localTime} - {backup.Name}";
    }

    private static bool TryParseBackupTimestamp(string fileName, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (!fileName.StartsWith(DatabaseService.BackupFilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(BackupFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var core = fileName.Substring(DatabaseService.BackupFilePrefix.Length,
            fileName.Length - DatabaseService.BackupFilePrefix.Length - BackupFileSuffix.Length);
        return DateTimeOffset.TryParseExact(
            core,
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
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

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2, string title = "")
    {
        title = title.Trim();
        message = message.Trim();
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            PageInfoBar.ActionButton = null;
            InfoBarWidth.Update(InfobarContent, hold: false);
            //PageInfoBar.Title = title;

            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>   {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
        });
        dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
        dispatcherTimer.Start();
    }

    private void ShowInfoBarPermanent(InfoBarSeverity severity, string message, string title = "", string buttonText = "")
    {
        title = title.Trim();
        message = message.Trim();
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            PageInfoBar.ActionButton = null;
            InfoBarWidth.Update(InfobarContent, hold: IsConverting); // progress text changes during a run

            //PageInfoBar.Title = title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>    {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
        });
    }

    private void ForceHideInfoBar()
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            PageInfoBar.Opacity = 0;
            if (dispatcherTimer.IsEnabled)
            {
                dispatcherTimer.Stop();
            }

            // Set IsOpen to false after 0.25 seconds
            Task.Factory.StartNew(() =>
            {
                System.Threading.Thread.Sleep(250);
                try
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        PageInfoBar.IsOpen = false;
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            });
        });
    }

    private void ShowSignInRequired(SourceSignInRequiredException error)
    {
        if (SettingsNavigation.ForSource(error.SourceId) is not { } section) return;
        ShowSettingsNotice(error.Message, section);
    }

    private void ShowSettingsNotice(string message, SettingsSection section)
    {
        dispatcherTimer.Stop();
        ShowInfoBarPermanent(InfoBarSeverity.Warning, SettingsNavigation.WithLink(message, section));
        dispatcherQueue.TryEnqueue(() =>
        {
            InfobarProgress.Visibility = Visibility.Collapsed;
        });
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void DispatcherTimer_Tick(object? sender, object? e)
    {
        var timer = sender as DispatcherTimer;
        if (timer == null || !timer.IsEnabled) return; // Already closed

        PageInfoBar.Opacity = 0;
        timer.Stop();

        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            try // Can crash if app is closed
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    PageInfoBar.IsOpen = false;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        });
    }

    private void OutputDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string text;
        OutputTextBox.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out text); // Get the text from the RichEditBox
        Clipboard.CopyToClipboard(text);
        ShowInfoBar(InfoBarSeverity.Success, "Copied to clipboard");
    }

    private string? GetOutputSource()
    {
        return OutputComboBox.SelectedItem as string;
    }

    // The output list shows display names; this maps the selected one back to its source id.
    private string? GetOutputSourceId()
    {
        var displayName = GetOutputSource();
        return SourceCatalog.All.FirstOrDefault(s => s.DisplayName == displayName)?.Id;
    }

    private async void ConvertDialogOpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Pre-select existing sources
        var sources = new HashSet<string>();
        foreach (var song in (ObservableCollection<QueueObject>)CustomListView.ItemsSource)
        {
            sources.Add(song.Source);
        }

        // Only sources that are in the queue can be selected
        foreach (var option in ConversionOptions)
        {
            option.IsChecked = sources.Contains(option.Id);
            option.IsEnabled = option.IsChecked == true;
        }

        // Show conversion dialog
        ConversionDialog.XamlRoot = this.XamlRoot;
        await ConversionDialog.ShowAsync();
    }

    private static readonly Windows.UI.Color BadgeSuccess = Windows.UI.Color.FromArgb(255, 108, 203, 95);
    private static readonly Windows.UI.Color BadgeWarning = Windows.UI.Color.FromArgb(255, 252, 225, 0);
    private static readonly Windows.UI.Color BadgeError = Windows.UI.Color.FromArgb(255, 255, 153, 164);

    /// <summary>
    /// Downloads a queued album, then replaces its row with one row per track in album order: the local file for each
    /// downloaded track, or the streaming track with an error badge for one that failed, so it can be retried.
    /// </summary>
    private async Task DownloadQueuedAlbumAsync(QueueObject row, string directory, IProgress<ProgressData> progress, ConversionUpdateCallback callback)
    {
        var album = (AlbumSearchObject)QueueObject.Unwrap(row);
        var downloaded = new ConcurrentDictionary<string, StatusLevel>(StringComparer.OrdinalIgnoreCase); // file -> result
        var failedIds = new ConcurrentDictionary<string, byte>();
        List<string> paths;
        try
        {
            paths = await downloads.DownloadAlbum(album, directory, progress, (level, track, location) =>
            {
                callback(level, track, location);
                if (level == StatusLevel.Error) failedIds.TryAdd(track.Source + track.Id, 0);
                else if (!string.IsNullOrEmpty(location)) downloaded[location] = level;
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Queued album download failed");
            callback(StatusLevel.Error, row);
            dispatcherQueue.TryEnqueue(() =>
            {
                row.ConvertBadgeColor = new SolidColorBrush(BadgeError);
                ShowInfoBar(InfoBarSeverity.Error, $"{row.Title}: {ex.Message}", 5);
            });
            return;
        }

        var tracks = album.TrackList ?? [];
        var files = paths.Concat(downloaded.Keys).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var done = new TaskCompletionSource();
        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var entries = new List<(int Order, SongSearchObject Song, Windows.UI.Color Badge)>();
                foreach (var file in files)
                {
                    var local = LocalExplorerViewModel.ParseFile(file);
                    if (local == null) continue;
                    local.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(file);
                    var badge = downloaded.TryGetValue(file, out var level) && level == StatusLevel.Warning ? BadgeWarning : BadgeSuccess;
                    entries.Add((AlbumOrder(tracks, local), local, badge));
                }
                entries.AddRange(tracks.Where(t => failedIds.ContainsKey(t.Source + t.Id)).Select(t => (tracks.IndexOf(t), t, BadgeError)));
                if (entries.Count == 0)
                {
                    row.ConvertBadgeColor = new SolidColorBrush(BadgeError);
                    return;
                }

                var rows = new List<QueueObject>();
                foreach (var (entry, i) in entries.OrderBy(e => e.Order).Select((e, i) => (e, i)))
                {
                    var created = await QueueViewModel.CreateQueueObject(entry.Song, row.QueueCounter, i)
                        ?? new QueueObject(entry.Song) { QueueCounter = row.QueueCounter, QueueSubOrder = i };
                    created.ConvertBadgeColor = new SolidColorBrush(entry.Badge);
                    rows.Add(created);
                }
                await QueueViewModel.ReplaceWithMany(QueueViewModel.Source.IndexOf(row), rows);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Could not show the downloaded album's tracks in the queue");
                row.ConvertBadgeColor = new SolidColorBrush(BadgeWarning);
            }
            finally
            {
                done.TrySetResult();
            }
        });
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // A downloaded file's place in the album: the track with the same ISRC, else the same title, else its track number
    // after the listed tracks.
    internal static int AlbumOrder(List<SongSearchObject> tracks, SongSearchObject local)
    {
        var index = tracks.FindIndex(t => !string.IsNullOrWhiteSpace(local.Isrc) && string.Equals(t.Isrc, local.Isrc, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            var title = Core.Matching.TitleNormalizer.Compact(local.Title);
            index = tracks.FindIndex(t => Core.Matching.TitleNormalizer.Compact(t.Title) == title);
        }
        if (index >= 0) return index;
        var digits = new string((local.TrackPosition ?? "").TakeWhile(char.IsDigit).ToArray());
        return tracks.Count + (int.TryParse(digits, out var number) ? number : 9999);
    }

    private async Task ConversionTask(IEnumerable<string> selectedSources, string outputSource)
    {
        var songsWithOriginalIndex = QueueViewModel.Source
            .Select((song, originalIndex) => new { Song = song, OriginalIndex = originalIndex });
        var songsToConvert = songsWithOriginalIndex
            .Where(x => selectedSources.Contains(x.Song.Source) && outputSource != x.Song.Source);

        int totalCount = songsToConvert.Count();
        if (totalCount == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, $"No tracks to {(outputSource == SourceIds.Local ? "download" : "convert")}", 3);
            return;
        }

        if (outputSource == SourceIds.Local)
        {
            try { await downloads.EnsureDownloadAccessAsync(songsToConvert.Select(item => (SongSearchObject)item.Song)); }
            catch (SourceSignInRequiredException error) { ShowSignInRequired(error); return; }
        }

        // Set download location if output is local and ask before download is enabled or download directory is not set
        var directory = await SettingsViewModel.GetSetting<string>(SettingsViewModel.DownloadDirectory);
        if (outputSource == SourceIds.Local && (await SettingsViewModel.GetSetting<bool>(SettingsViewModel.AskBeforeDownload) || string.IsNullOrWhiteSpace(directory)))
        {
            directory = await StoragePickerHelper.GetDirectory();
            if (directory == null)
            {
                ShowSettingsNotice("No download directory selected.", SettingsSection.Downloads);
                return;
            }
        }

        // Clear preview pane when source is being edited
        PreviewPanel.Clear();

        // Clear queue command running progress
        QueueViewModel.Reset(); // Reset the queue object result strings and index
        // Reset back to normal
        ProgressTextButton.Visibility = Visibility.Collapsed;
        CountTextButton.Visibility = Visibility.Visible;
        QueueProgress.Value = 0;

        cancellationTokenSource = new CancellationTokenSource(); // Create a new cancellation token source

        // Clear the conversion results
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        QueueViewModel.ResetConversionResults(); // Clear all badges and results on queue objects
        ViewModel.SuccessCount = ViewModel.WarningCount = ViewModel.ErrorCount = 0; // Reset the counts

        void conversionUpdateCallback(StatusLevel level, SongSearchObject song, string? location = null)
        {
            switch (level)
            {
                case StatusLevel.Success:
                    successSource.TryAdd(song, 0);
                    break;
                case StatusLevel.Warning:
                    warningSource.TryAdd(song, 0);
                    break;
                case StatusLevel.Error:
                    errorSource.TryAdd(song, 0);
                    break;
                default:
                    throw new Exception("Unspecified severity in callback");
            }
        }

        // For to local output. Each album counts as its tracks.
        var trackTotal = songsToConvert.Sum(x => x.Song.AlbumTracksCount is > 0 and var n ? n : 1);
        var downloadHelper = new DownloadProgressHelper($"Queue to <a href='{directory}'>{directory}</a>", trackTotal);
        downloadHelper.ProgressUpdated += (sender, args) =>
        {
            var finalStr = args.DisplayString;
            var progress = args.ProgressValue;
            var preparing = args.Preparing;
            dispatcherQueue.TryEnqueue(() =>
            {
                if (!downloadHelper.IsRunning) return;
                QueueProgress.IsIndeterminate = preparing;
                QueueProgress.Value = Math.Clamp(progress * 100, 0, 100);
                ShowInfoBarPermanent(InfoBarSeverity.Informational, finalStr, title: "Downloading");
            });
        };
        var progress = new Progress<ProgressData>(downloadHelper.UpdateProgressLoop);

        try
        {
            // Infobar notification
            if (outputSource == SourceIds.Local)
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Saving {trackTotal} tracks to <a href='{directory}'>{directory}</a>", title: "Download in Progress");
            }
            else
            {
                ShowInfoBarPermanent(InfoBarSeverity.Informational, $"Converting selected input sources to {GetOutputSource()}", title: "Conversion in Progress");
            }

            // Prepare UI
            DownloadAllButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = RestoreQueueButton.Visibility = CreatePlaylistButton.Visibility = Visibility.Collapsed; // Hide buttons
            ConvertStopButton.Visibility = Visibility.Visible; // Show stop button

            // Other initialization
            if (outputSource == SourceIds.Local)
            {
                downloadHelper.StartLoop();
            }
            IsConverting = true;
            int processedCount = 0;
            var token = cancellationTokenSource.Token;
            var conversionClock = Stopwatch.StartNew();
            var conversionEta = new EtaEstimator();

            var downloadThreads = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.ConversionThreads) ?? 1;
            using var semaphore = new SemaphoreSlim(downloadThreads);

            var tasks = songsToConvert.Select(async (x) =>
            {
                var song = x.Song;
                var index = x.OriginalIndex;
                try
                {
                    bool isReleased = false;
                    await semaphore.WaitAsync(token);
                    try
                    {
                        // Set song to running (progress ring will appear)
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            song.IsRunning = true;
                        });

                        // Convert the track and get new song object
                        SongSearchObject? newSongObj = null;
                        string? fileLocation = null;
                        if (outputSource == SourceIds.Local && song.IsAlbum)
                        {
                            await DownloadQueuedAlbumAsync(song, directory, progress, conversionUpdateCallback);
                            return; // the finally below releases the slot and counts the row
                        }
                        if (outputSource == SourceIds.Local) // Conversion to local, download the track
                        {
                            fileLocation = await downloads.DownloadObject(song, directory, progress, conversionUpdateCallback);
                            newSongObj = song;
                        }
                        else
                        {
                            var match = await conversions.FindEquivalentAsync(QueueObject.Unwrap(song), outputSource, token);
                            switch (match.Kind)
                            {
                                case MatchKind.Exact:
                                    conversionUpdateCallback(StatusLevel.Success, match.Track!);
                                    break;
                                case MatchKind.Close:
                                    conversionUpdateCallback(StatusLevel.Warning, match.Track!);
                                    break;
                                case MatchKind.NotFound:
                                    conversionUpdateCallback(StatusLevel.Error, song);
                                    break;
                            }
                            newSongObj = match.Track;
                        }

                        var tcs = new TaskCompletionSource();
                        // Update the UI with the new song object or set error badge if conversion failed
                        dispatcherQueue.TryEnqueue(async () =>
                        {
                            try
                            {
                                if (newSongObj != null)
                                {
                                    QueueObject? queueObj;
                                    if (outputSource == SourceIds.Local)
                                    {
                                        var localSong = LocalExplorerViewModel.ParseFile(fileLocation);

                                        if (localSong != null)
                                        {
                                            localSong.LocalBitmapImage = await LocalExplorerViewModel.GetBitmapImageAsync(fileLocation);
                                            queueObj = await QueueViewModel.CreateQueueObject(localSong, song.QueueCounter);
                                            await QueueViewModel.Replace(QueueViewModel.Source.IndexOf(song), queueObj);
                                        }
                                        else // Can happen when trying to parse opus files
                                        {
                                            queueObj = song; // Set to the initial object so badge is shown below
                                        }
                                    }
                                    else
                                    {
                                        queueObj = await QueueViewModel.CreateQueueObject(newSongObj, song.QueueCounter);
                                        await QueueViewModel.Replace(QueueViewModel.Source.IndexOf(song), queueObj);
                                    }

                                    if (queueObj != null)
                                    {
                                        if (successSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                                        else if (warningSource.ContainsKey(newSongObj)) queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 225, 0));
                                        else // Assume failure
                                        {
                                            queueObj.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                        }
                                    }
                                }
                                else // Failed conversion, set current object badge to error
                                {
                                    song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 153, 164));
                                }
                            }
                            finally
                            {
                                tcs.TrySetResult();
                            }
                        });

                        // No need to wait for dispatcher/ui update to complete
                        semaphore.Release();
                        isReleased = true;

                        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            // Exception color code
                            song.ConvertBadgeColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0));
                        });
                    }
                    finally
                    {
                        var processCaptured = Interlocked.Increment(ref processedCount); // Increment the processed count
                        var elapsedCaptured = conversionClock.Elapsed;
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            song.IsRunning = false; // Set song to not running
                            if (outputSource != SourceIds.Local && IsConverting)
                            {
                                QueueProgress.Value = 100.0 * processCaptured / totalCount; // Update the progress bar
                                var eta = conversionEta.Update((double)processCaptured / totalCount, elapsedCaptured);
                                ShowInfoBarPermanent(InfoBarSeverity.Informational,
                                    $"Converting to {GetOutputSource()} \u2022 {DownloadProgressHelper.FormatCountedPercent(QueueProgress.Value, processCaptured, totalCount)}{(eta != null ? " \u2022 " + eta : "")}",
                                    title: "Conversion in Progress");
                            }
                        });

                        if (!isReleased)
                        {
                            semaphore.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Handle cancellation if needed
                }
            });

            await Task.WhenAll(tasks);

            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.Notifications) ?? false)
            {
                App.GetService<IAppNotificationService>().Show(string.Format("QueueCompletePayload".GetLocalized(), AppContext.BaseDirectory));
            }
        }
        finally
        {
            // Resetting UI
            dispatcherQueue.TryEnqueue(() =>
            {
                QueueProgress.IsIndeterminate = false;
                QueueProgress.Value = 0;  // Ensures this happens after currently queued progress changes
            });

            DownloadAllButton.Visibility = ConvertDialogOpenButton.Visibility = ClearButton.Visibility = RestoreQueueButton.Visibility = CreatePlaylistButton.Visibility = Visibility.Visible; // Show buttons
            ConvertStopText.Text = "Stop"; // Reset stop button text
            ConvertStopButton.Visibility = Visibility.Collapsed; // Hide stop button
            IsConverting = false;

            if (outputSource == SourceIds.Local)
            {
                downloadHelper.StopLoop();
            }

            ForceHideInfoBar();

            await ShowConversionDialog();
        }
    }

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        string outputSource = SourceIds.Local;
        var selectedSources = SourceCatalog.All.Where(s => s.Id != SourceIds.Local).Select(s => s.Id).ToList(); // All sources except local

        await ConversionTask(selectedSources, outputSource);
    }

    private async void ConversionDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Check for valid input
        if (GetOutputSource() == null)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Please select an output source.", 3);
            return;
        }

        if (ConversionOptions.All(option => option.IsChecked == false))
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Please select at least one input source.", 3);
            return;
        }

        var outputSource = GetOutputSourceId()!;
        var selectedSources = ConversionOptions.Where(option => option.IsChecked == true).Select(option => option.Id).ToHashSet();

        await ConversionTask(selectedSources, outputSource);
    }

    private void ConvertStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsConverting) // If currently converting
        {
            cancellationTokenSource.Cancel(); // Cancel the conversion
            ConvertStopText.Text = "Stopping ...";
        }
    }

    private void ConversionTabView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = ConversionTabView.SelectedItem as TabViewItem;
        // Set items source based on selected tab
        if (selectedItem == SuccessTab)
        {
            ConversionListView.ItemsSource = successSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Success;
            TabInfoBar.Content = "Exact conversions using ISRC or other ids";
            NoConversionText.Visibility = successSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == WarningTab)
        {
            ConversionListView.ItemsSource = warningSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Warning;
            TabInfoBar.Content = "Attempted conversions using metadata";
            NoConversionText.Visibility = warningSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (selectedItem == ErrorTab)
        {
            ConversionListView.ItemsSource = errorSource.Keys;
            TabInfoBar.Severity = InfoBarSeverity.Error;
            TabInfoBar.Content = "Failed conversions";
            NoConversionText.Visibility = errorSource.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task ShowConversionDialog()
    {
        ConversionResultsDialog.XamlRoot = this.XamlRoot;

        // Set the counts
        ViewModel.SuccessCount = successSource.Count;
        ViewModel.WarningCount = warningSource.Count;
        ViewModel.ErrorCount = errorSource.Count;

        if (ViewModel.SuccessCount > 0)
        {
            ConversionTabView.SelectedItem = SuccessTab;
        }
        else if (ViewModel.WarningCount > 0)
        {
            ConversionTabView.SelectedItem = WarningTab;
        }
        else if (ViewModel.ErrorCount > 0)
        {
            ConversionTabView.SelectedItem = ErrorTab;
        }

        try
        {
            // Show the dialog
            await ConversionResultsDialog.ShowAsync();
        }
        catch (Exception e)
        {
            Debug.WriteLine("Queue conversion dialog fail: " + e.Message);
        }
    }
}
