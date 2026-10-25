using FluentDL.Contracts.Services;
using FluentDL.Core.Services;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentDL;

// Only the explicit compact-layout fixture generates these local files and its test toolbar.
internal static class CompactLayoutTestSupport
{
    internal static void Attach(UIElement? content)
    {
        var shell = (ShellPage)content!;
        var frame = (Frame)shell.FindName("NavigationFrame");
        var root = (Grid)shell.Content;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var child in root.Children) Grid.SetRow((FrameworkElement)child, ((FrameworkElement)child).Name == "AppTitleBar" ? 0 : 2);
        var controls = new StackPanel { Spacing = 4, Margin = new Thickness(16, 4, 16, 8), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(controls, 1);
        var buttons = new CommunityToolkit.WinUI.UI.Controls.WrapPanel { Orientation = Orientation.Horizontal,
            HorizontalSpacing = 8, VerticalSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var status = new TextBlock { MaxWidth = 1300, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "CompactFixtureStatus");
        controls.Children.Add(buttons);
        controls.Children.Add(status);
        root.Children.Add(controls);
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
        App.MainWindow.AppWindow.Move(new Windows.Graphics.PointInt32(20, 20));
        var songs = new List<SongSearchObject>();
        object? expectedSource = null;
        object? expectedSelection = null;
        BitmapImage? artwork = null;
        LocalExplorerPage Local() => (LocalExplorerPage)frame.Content;
        ListView List() => (ListView)Local().FindName("FileListView");
        void Add(string id, string label, RoutedEventHandler action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += action;
            buttons.Children.Add(button);
        }
        void Read()
        {
            var local = App.GetService<LocalDisplaySettings>();
            var queue = App.GetService<QueueDisplaySettings>();
            var prefix = $"local={(local.IsCompact ? "list" : "cards")}; queue={(queue.IsCompact ? "list" : "cards")}";
            if (frame.Content is LocalExplorerPage page)
            {
                var list = (ListView)page.FindName("FileListView");
                var panel = list.ItemsPanelRoot as ItemsStackPanel;
                var menu = (MenuFlyout)page.FindName("CompactLocalMenu");
                var target = menu.Items[0].Tag as SongSearchObject;
                var selected = list.SelectedItem as SongSearchObject;
                var preview = (PreviewPane)page.FindName("PreviewPanel");
                status.Text = prefix + $"; count={list.Items.Count}; first={panel?.FirstVisibleIndex ?? -1}; realized={panel?.Children.Count ?? 0}"
                    + $"; selected={(selected == null ? -1 : songs.IndexOf(selected))}; context={(target == null ? -1 : songs.IndexOf(target))}"
                    + $"; source={(expectedSource == null || ReferenceEquals(expectedSource, list.ItemsSource) ? "same" : "changed")}"
                    + $"; selection={(ReferenceEquals(expectedSelection, list.SelectedItem) ? "same" : "changed")}; art={(songs.Count > 0 && ReferenceEquals(songs[0].LocalBitmapImage, artwork) ? "same" : "changed")}";
                status.Text += $"; preview={(preview.Visibility == Visibility.Visible ? "visible" : "hidden")}; previewRow={Grid.GetRow(preview)}";
            }
            else status.Text = prefix + $"; queueCount={QueueViewModel.Source.Count}";
        }
        Add("PrepareCompactFixture", "Prepare", async (_, _) =>
        {
            songs = await Task.Run(GenerateTracks);
            artwork = new BitmapImage(new Uri("ms-appx:///Assets/Unloaded.jpg")) { DecodePixelHeight = 76 };
            LocalExplorerViewModel.ClearSongs();
            foreach (var song in songs)
            {
                if (song.QueueCounter != 3) song.LocalBitmapImage = artwork;
                LocalExplorerViewModel.AddSong(song);
            }
            App.GetService<INavigationService>().NavigateTo(typeof(LocalExplorerViewModel).FullName!);
            Read();
        });
        Add("CaptureCompactState", "Snapshot", (_, _) => { expectedSource = List().ItemsSource; expectedSelection = List().SelectedItem; Read(); });
        Add("SelectCompactSample", "Select", (_, _) => { List().SelectedItem = songs[10]; List().ScrollIntoView(songs[10], ScrollIntoViewAlignment.Leading); });
        Add("ScrollCompactFar", "Scroll far", (_, _) => List().ScrollIntoView(songs[1500], ScrollIntoViewAlignment.Leading));
        Add("CompactFixtureWide", "Wide", (_, _) => App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1900, 1000)));
        Add("CompactFixtureNarrow", "Narrow", (_, _) => App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 900)));
        Add("CompactFixtureDark", "Dark", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Dark));
        Add("CompactFixtureLight", "Light", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Light));
        Add("CompactLocalResults", "Local results", async (_, _) =>
        {
            Local().ConversionResults.Clear();
            foreach (var song in songs.Take(10)) Local().ConversionResults.Add(new(song, song.QueueCounter == 9 ? null : song.Id));
            var dialog = (ContentDialog)Local().FindName("ConversionDialog");
            dialog.XamlRoot = Local().XamlRoot;
            ((InfoBar)Local().FindName("ConversionInfobar")).Severity = InfoBarSeverity.Warning;
            ((TextBlock)Local().FindName("ConversionInfobarText")).Text = "Conversion complete. Converted 9 of 10 tracks. 1 failed.";
            ((ListView)Local().FindName("ConversionListView")).ItemsSource = Local().ConversionResults;
            await dialog.ShowAsync();
        });
        Add("CompactQueueResults", "Queue results", async (_, _) =>
        {
            App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            var page = (QueuePage)frame.Content;
            if (!page.IsLoaded)
            {
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Ready(object sender, RoutedEventArgs args) => loaded.TrySetResult();
                page.Loaded += Ready;
                try { await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { page.Loaded -= Ready; }
            }
            await page.ShowCompactSummaryFixtureAsync(songs.Where(song => song.LocalBitmapImage != null).Take(15).ToList());
        });
        Add("CloseCompactFixture", "Close", (_, _) => App.MainWindow.Close());
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => Read();
        timer.Start();
        App.MainWindow.Closed += (_, _) => timer.Stop();
        Read();
    }

    private static List<SongSearchObject> GenerateTracks()
    {
        var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "compact-fixture");
        Directory.CreateDirectory(folder);
        var tracks = new List<SongSearchObject>();
        for (var index = 0; index < 2000; index++)
        {
            var path = Path.Combine(folder, $"local-track-{index:D4}.wav");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + 1600); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(1600); writer.Write(new byte[1600]);
            }
            var track = LocalExplorerViewModel.ParseFile(path) ?? throw new InvalidOperationException("The fixture WAV could not be read.");
            track.Title = $"Local track {index:D4}";
            track.Artists = "Fixture artist " + index % 3;
            track.AlbumName = "Fixture album " + index % 5;
            track.ReleaseDate = "2024-01-01";
            track.Duration = "239";
            track.AudioFormat = index % 2 == 0 ? "FLAC" : "MP3";
            track.QueueCounter = index;
            track.ImageLocation = "https://must-not-fetch.invalid/cover.jpg";
            tracks.Add(track);
        }
        return tracks;
    }
}
