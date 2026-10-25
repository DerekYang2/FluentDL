using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentDL.Contracts.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentDL;

// Synthetic metadata and loopback artwork only. Included in the isolated Sandbox fixture build.
internal static class QueueLoadTestSupport
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private const int Count = 1200;
    private const string Origin = "http://localhost:18764/";
    private static int requests, active, peak;
    private static int urlRequests, forbiddenRequests;
    private static bool failCover = true;
    private static int delay = 200;
    private static readonly object closeLogLock = new();

    internal static void Attach(UIElement? content)
    {
        var shell = (ShellPage)content!;
        var frame = (Frame)shell.FindName("NavigationFrame");
        var root = (Grid)shell.Content;
        root.RowDefinitions.Add(new() { Height = new GridLength(48) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var child in root.Children) Grid.SetRow((FrameworkElement)child, ((FrameworkElement)child).Name == "AppTitleBar" ? 0 : 2);
        var toolbar = new StackPanel { Spacing = 4, Margin = new Thickness(16, 4, 16, 8) };
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);
        var buttons = new CommunityToolkit.WinUI.UI.Controls.WrapPanel { Orientation = Orientation.Horizontal, HorizontalSpacing = 8, VerticalSpacing = 4 };
        toolbar.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "QueueLoadStatus");
        toolbar.Children.Add(status);
        var phase = "ready";
        var marker = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "queue-load-allow-missing");
        var closeLog = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "queue-load-close-exceptions.log");
        failCover = !File.Exists(marker);
        var loader = typeof(QueueViewModel).GetProperty("Artwork", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        ((HttpClient)loader.GetType().GetField("http", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loader)!)
            .DefaultRequestHeaders.Add("X-FluentDL-Queue-Load-Test", "1");
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (QueueViewModel.IsClosing && args.Exception is COMException
                && (args.Exception.StackTrace?.Contains("QueueArtworkLoader") == true || args.Exception.StackTrace?.Contains("QueueArtworkTile") == true))
                lock (closeLogLock) File.AppendAllText(closeLog, $"Queue artwork COM exception: 0x{args.Exception.HResult:X8}\n");
        };
        var persisted = -1;
        var covers = -1;
        var add = new Stopwatch();
        var navigation = new Stopwatch();
        long addMs = -1, navMs = -1, maxGap = 0;
        var integrity = "unchecked";
        var backwards = 0;
        var indeterminate = false;
        var barValue = -1d;
        var previousBarPhase = "";
        var tiles = 0;
        var skeletons = 0;
        var pulsing = 0;
        var visibleSkeletons = 0;
        var tileWidth = 0;
        SongSearchObject? expectedResult = null;
        var resultShared = false;
        var urlReady = 0;
        var urlFailed = 0;
        QueuePage? stopPage = null;
        QueuePage.QueueStopProbe? stopProbe = null;
        var heartbeat = Stopwatch.StartNew();
        var lastBeat = heartbeat.ElapsedMilliseconds;
        void Read()
        {
            var shared = QueueViewModel.Source.Where(row => row.ImageLocation == Origin + "cover-0.jpg").Select(row => row.LocalBitmapImage).ToArray();
            var same = shared.Length > 1 && shared[0] != null && shared.All(image => ReferenceEquals(image, shared[0]));
            var ordered = QueueViewModel.Source.Select((row, index) => row.QueueCounter == index).All(value => value);
            var isrc = QueueViewModel.Source.All(row => row.Isrc == "USABC2400001");
            status.Text = $"phase={phase}; count={QueueViewModel.Source.Count}; requests={requests}; active={active}; peak={peak}; urlRequests={urlRequests}; forbidden={forbiddenRequests}; urlReady={urlReady}; urlFailed={urlFailed}; maxGap={maxGap}; addMs={addMs}; navMs={navMs}; saved={persisted}; covers={covers}; shared={same}; entries={QueueViewModel.ArtworkEntryCount}; bulk={QueueViewModel.IsBulkAdding}; failed={QueueViewModel.BulkArtworkFailures}; ordered={ordered}; isrc={isrc}; integrity={integrity}; barIndeterminate={indeterminate}; barValue={barValue:F0}; backwards={backwards}; tiles={tiles}; skeletons={skeletons}; pulsing={pulsing}; visibleSkeletons={visibleSkeletons}; tileWidth={tileWidth}; resultShared={resultShared}; progress={QueueViewModel.BulkAddText}";
            if (stopPage != null && stopProbe != null) status.Text += "; " + stopPage.InspectStopFixture(stopProbe);
        }
        void Add(string id, string label, RoutedEventHandler action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += action;
            buttons.Children.Add(button);
        }
        async Task Prepare(int count)
        {
            await QueueViewModel.Clear();
            File.WriteAllText(closeLog, "");
            if (count == Count)
            {
                File.Delete(marker);
                failCover = true;
            }
            App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            App.GetService<INavigationService>().NavigateTo(typeof(SearchViewModel).FullName!);
            var search = (Search)frame.Content;
            var list = (ListView)search.FindName("CustomListView");
            list.ItemsSource = new ObservableCollection<SongSearchObject>(Enumerable.Range(0, count).Select(index => new SongSearchObject
            {
                Id = "queue-load-" + index, Title = $"Queue load track {index:D4}", Artists = "Fixture artist", AlbumName = "Shared fixture album",
                Source = new[] { SourceIds.Deezer, SourceIds.Spotify, SourceIds.Qobuz, SourceIds.YouTube }[index % 4],
                Isrc = "USABC2400001", Duration = "239", ReleaseDate = "2024-01-02",
                ImageLocation = Origin + (index % 20 == 19 ? "missing.jpg" : $"cover-{index % 20}.jpg"),
            }));
            ((TextBlock)search.FindName("NoSearchResults")).Visibility = Visibility.Collapsed;
            search.SetResultsAmount(count);
            phase = "prepared";
            Read();
        }
        Add("QueueLoadPrepare", "Prepare 1200", async (_, _) => await Prepare(Count));
        Add("QueueLoadSmall", "Prepare 5", async (_, _) => await Prepare(5));
        Add("QueueLoadSlow", "Slow covers", (_, _) => delay = 2000);
        Add("QueueLoadArm", "Measure", (_, _) =>
        {
            requests = 0;
            peak = active;
            maxGap = 0;
            addMs = navMs = -1;
            add.Reset();
            backwards = 0;
            indeterminate = false;
            barValue = -1;
            previousBarPhase = "";
            lastBeat = heartbeat.ElapsedMilliseconds;
            phase = "measuring";
            Read();
        });
        Add("QueueLoadNavigate", "Open Queue", (_, _) =>
        {
            navigation.Restart();
            App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            App.MainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                navMs = navigation.ElapsedMilliseconds;
                phase = "queue";
                Read();
            });
        });
        Add("QueueLoadInspect", "Check storage", async (_, _) =>
        {
            var path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "fluentdl.db");
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), SUM(CASE WHEN Image IS NOT NULL THEN 1 ELSE 0 END) FROM Items";
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync()) { persisted = reader.GetInt32(0); covers = reader.IsDBNull(1) ? 0 : reader.GetInt32(1); }
            reader.Close();
            command.CommandText = "PRAGMA integrity_check";
            integrity = (await command.ExecuteScalarAsync())?.ToString() ?? "unknown";
            Read();
        });
        Add("QueueLoadAllowCover", "Allow failed cover", (_, _) => { failCover = false; File.WriteAllText(marker, "allow"); });
        Add("QueueLoadExpand", "Expand cached album", async (_, _) =>
        {
            var image = QueueViewModel.Source[0].LocalBitmapImage;
            var tracks = Enumerable.Range(1, 2).Select(index => new SongSearchObject
            {
                Source = SourceIds.Deezer, Id = "queue-expanded-" + index, Title = "Expanded album track " + index,
                Artists = "Fixture artist", ImageLocation = Origin + "expanded-cover.jpg", LocalBitmapImage = image,
                Duration = "239", Isrc = "USABC2400001", TrackPosition = index.ToString(),
            }).ToArray();
            var album = QueueViewModel.Add(new AlbumSearchObject
            {
                Source = SourceIds.Deezer, Id = "queue-load-album", Title = "Fixture album", TracksCount = 2,
                ImageLocation = Origin + "cover-0.jpg", LocalBitmapImage = image,
            })!;
            await QueueViewModel.ExpandAlbumAsync(album, tracks);
            ((ListView)((QueuePage)frame.Content).FindName("CustomListView")).ScrollIntoView(QueueViewModel.Source.Last(), ScrollIntoViewAlignment.Leading);
            phase = "expanded";
            Read();
        });
        Add("QueueLoadClear", "Clear Queue", async (_, _) => { await QueueViewModel.Clear(); phase = "cleared"; Read(); });
        Add("QueueSkeletonPreview", "Skeleton states", async (_, _) =>
        {
            await QueueViewModel.Clear();
            File.WriteAllText(closeLog, "");
            await App.GetService<FluentDL.Core.Services.QueueDisplaySettings>().SetCompactAsync(true);
            var image = new BitmapImage(new Uri("ms-appx:///Assets/FluentDL_logo.png"));
            var titles = new[] { "Loading cover", "Cached cover, shown immediately", "No cover available", "Loading another cover", "Loading shared cover" };
            for (var index = 0; index < titles.Length; index++)
            {
                var row = QueueViewModel.Add(new SongSearchObject
                {
                    Source = SourceIds.Deezer, Id = "skeleton-" + index, Title = titles[index], Artists = "Fixture artist",
                    ImageLocation = "ms-appx:///Assets/Unloaded.jpg", LocalBitmapImage = index == 1 ? image : null,
                    Isrc = "USABC2400001", Duration = "239",
                })!;
                if (index is 0 or 3 or 4) row.ArtworkState = QueueArtworkState.Loading;
            }
            App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            phase = "skeleton-preview";
        });
        Add("QueueSkeletonMany", "1200 skeletons", async (_, _) =>
        {
            await QueueViewModel.Clear();
            await App.GetService<FluentDL.Core.Services.QueueDisplaySettings>().SetCompactAsync(true);
            for (var index = 0; index < Count; index++)
            {
                var row = QueueViewModel.Add(new SongSearchObject
                {
                    Source = SourceIds.Deezer, Id = "skeleton-many-" + index, Title = $"Skeleton track {index:D4}", Artists = "Fixture artist",
                    ImageLocation = "ms-appx:///Assets/Unloaded.jpg", Isrc = "USABC2400001", Duration = "239",
                })!;
                row.ArtworkState = QueueArtworkState.Loading;
            }
            phase = "skeleton-many";
        });
        Add("QueueUrlPreview", "URL skeletons", async (_, _) =>
        {
            await QueueViewModel.Clear();
            delay = 3000;
            failCover = true;
            requests = urlRequests = forbiddenRequests = 0;
            var image = new BitmapImage(new Uri("ms-appx:///Assets/FluentDL_logo.png"));
            App.GetService<INavigationService>().NavigateTo(typeof(SearchViewModel).FullName!);
            var search = (Search)frame.Content;
            ((ListView)search.FindName("CustomListView")).ItemsSource = new ObservableCollection<SongSearchObject>(new[]
            {
                new SongSearchObject { Source = SourceIds.Deezer, Id = "url-ready", Title = "URL cover loading", Artists = "Fixture artist", ImageLocation = Origin + "lazy-ready.jpg" },
                new SongSearchObject { Source = SourceIds.Deezer, Id = "url-failed", Title = "URL cover fails", Artists = "Fixture artist", ImageLocation = Origin + "missing.jpg" },
                new SongSearchObject { Source = SourceIds.Deezer, Id = "url-cached", Title = "Cached cover avoids the URL", Artists = "Fixture artist", ImageLocation = Origin + "must-not-request.jpg", LocalBitmapImage = image },
            });
            ((TextBlock)search.FindName("NoSearchResults")).Visibility = Visibility.Collapsed;
            search.SetResultsAmount(3);
            phase = "url-preview";
        });
        Add("QueueResultPreview", "Cached results", async (_, _) =>
        {
            await QueueViewModel.Clear();
            requests = urlRequests = forbiddenRequests = 0;
            delay = 3000;
            var image = new BitmapImage(new Uri("ms-appx:///Assets/FluentDL_logo.png"));
            QueueViewModel.Add(new SongSearchObject { Source = SourceIds.Spotify, Id = "before-conversion", Title = "Original track", LocalBitmapImage = image });
            expectedResult = new SongSearchObject
            {
                Source = SourceIds.Deezer, Id = "converted", Title = "Converted track uses queue artwork", Artists = "Fixture artist",
                ImageLocation = Origin + "converted.jpg", Isrc = "USABC2400001", Duration = "239",
            };
            var converted = await QueueViewModel.CreateQueueObject(expectedResult, 0);
            await QueueViewModel.Replace(0, converted);
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
            phase = "result-preview";
            await page.ShowArtworkResultFixtureAsync(expectedResult);
        });
        Add("QueueDuplicatePreview", "Conversion duplicates", async (_, _) =>
        {
            phase = "duplicates-running";
            try
            {
                foreach (var reverse in new[] { false, true })
                {
                    await QueueViewModel.Clear();
                    requests = 0;
                    delay = 200;
                    SongSearchObject Input(string source, string id) => new()
                    {
                        Source = source, Id = id, Title = id, Artists = "Fixture artist", Duration = "239", Isrc = "USABC2400001",
                    };
                    var first = QueueViewModel.Add(Input(SourceIds.Qobuz, "first"))!;
                    QueueViewModel.Add(Input(SourceIds.Deezer, "middle"));
                    var last = QueueViewModel.Add(Input(SourceIds.Tidal, "last"))!;
                    foreach (var original in reverse ? new[] { last, first } : new[] { first, last })
                    {
                        var match = Input(SourceIds.Spotify, "same-spotify-id");
                        match.ImageLocation = Origin + "duplicate.jpg";
                        var replacement = await QueueViewModel.CreateQueueObject(match, original.QueueCounter);
                        await QueueViewModel.Replace(QueueViewModel.Source.IndexOf(original), replacement);
                    }
                    if (QueueViewModel.Source.Count != 2 || QueueViewModel.Source[0].Id != "same-spotify-id"
                        || QueueViewModel.Source[0].QueueCounter != 0 || QueueViewModel.Source[1].Id != "middle")
                        throw new InvalidOperationException("Duplicate conversion changed the retained row or order.");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (QueueViewModel.Source[0].ArtworkState != QueueArtworkState.Ready && DateTime.UtcNow < deadline) await Task.Delay(50);
                    await DatabaseService.FlushAsync();
                    if (requests != 1 || QueueViewModel.Source[0].ArtworkState != QueueArtworkState.Ready)
                        throw new InvalidOperationException("Merged rows did not share one artwork request.");
                    await QueueViewModel.LoadSaveQueue();
                    if (QueueViewModel.Source.Count != 2 || QueueViewModel.Source[0].Id != "same-spotify-id" || QueueViewModel.Source[0].QueueCounter != 0)
                        throw new InvalidOperationException("Restoring the merged queue changed its order.");
                    var restored = QueueViewModel.Source[0];
                    await QueueViewModel.Remove(restored);
                    await DatabaseService.FlushAsync();
                    var saved = await DatabaseService.LoadQueueJSON();
                    if (saved.Count != 1 || saved.ContainsKey(QueueViewModel.GetHash(restored)))
                        throw new InvalidOperationException("Removing the merged row left a saved duplicate.");
                    if (QueueViewModel.Add(restored) == null) throw new InvalidOperationException("Removal left a stale duplicate key.");
                }
                await DatabaseService.FlushAsync();
                await QueueViewModel.LoadSaveQueue();
                App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
                phase = "duplicates-pass";
            }
            catch (Exception error) { phase = "duplicates-failed-" + error.Message; }
            Read();
        });
        Add("QueueStopPreview", "Stop conversion", async (_, _) =>
        {
            var settings = App.GetService<ILocalSettingsService>();
            var threads = await settings.ReadSettingAsync<int?>(SettingsViewModel.ConversionThreads);
            phase = "stop-preparing";
            try
            {
                await settings.SaveSettingAsync(SettingsViewModel.ConversionThreads, 4);
                await QueueViewModel.Clear();
                await QueueViewModel.AddManyAsync(Enumerable.Range(0, 5000).Select(index => new SongSearchObject
                {
                    Source = SourceIds.Qobuz, Id = index switch { 0 => "stop-exact", 1 => "stop-not-found", 2 => "stop-error", _ => "stop-cancel-" + index },
                    Title = $"Stop test track {index:D4}", Artists = "Fixture artist", Duration = "239", Isrc = "USABC2400001",
                }).ToArray());
                stopProbe = new QueuePage.QueueStopProbe();
                stopPage = QueuePage.CreateStopFixture(stopProbe);
                frame.Content = stopPage;
                if (!stopPage.IsLoaded)
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Loaded(object sender, RoutedEventArgs args) => ready.TrySetResult();
                    stopPage.Loaded += Loaded;
                    try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                    finally { stopPage.Loaded -= Loaded; }
                }
                phase = "stop-running";
                maxGap = 0;
                await stopPage.RunStopFixtureAsync();
                await DatabaseService.FlushAsync();
                var saved = await DatabaseService.LoadQueueJSON();
                if (stopProbe.Calls != 7 || stopProbe.Cancelled != 4 || QueueViewModel.Source.Count != 5000
                    || !QueueViewModel.Source.Any(row => row.Id == "converted-stop-exact")
                    || QueueViewModel.Source.Any(row => row.Id == "converted-stop-cancel-3")
                    || saved.Count != 5000)
                    throw new InvalidOperationException("Stop lost completed work, accepted a late match or started additional rows.");
                phase = "stop-pass";
            }
            catch (Exception error) { phase = "stop-failed-" + error.Message; }
            finally { await settings.SaveSettingAsync(SettingsViewModel.ConversionThreads, threads); }
            Read();
        });
        Add("QueueSkeletonScroll", "Scroll last", (_, _) => ((ListView)((QueuePage)frame.Content).FindName("CustomListView"))
            .ScrollIntoView(QueueViewModel.Source.Last(), ScrollIntoViewAlignment.Leading));
        IEnumerable<QueueArtworkTile> FindTiles(DependencyObject node)
        {
            if (node is QueueArtworkTile tile) yield return tile;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in FindTiles(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        Add("QueueSkeletonInspect", "Inspect skeletons", (_, _) =>
        {
            var controls = FindTiles((DependencyObject)frame.Content)
                .Concat(VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Where(popup => popup.Child != null).SelectMany(popup => FindTiles(popup.Child)))
                .Where(tile => tile.IsLoaded).Distinct().ToArray();
            tiles = controls.Length;
            skeletons = controls.Count(tile => tile.IsSkeletonVisible);
            pulsing = controls.Count(tile => tile.IsPulsing);
            visibleSkeletons = controls.Count(tile => tile.IsSkeletonVisible && tile.IsInViewport);
            tileWidth = controls.Length > 0 ? (int)controls[0].ActualWidth : 0;
            urlReady = controls.Count(tile => tile.UseUrlFallback && tile.Item?.Id == "url-ready" && !tile.IsSkeletonVisible
                && tile.DisplayedSource is BitmapImage bitmap && bitmap.UriSource?.AbsoluteUri == Origin + "lazy-ready.jpg");
            urlFailed = controls.Count(tile => tile.UseUrlFallback && tile.Item?.Id == "url-failed" && !tile.IsSkeletonVisible
                && ReferenceEquals(tile.DisplayedSource, QueueViewModel.ArtworkPlaceholder));
            resultShared = expectedResult != null && controls.Any(tile => ReferenceEquals(tile.Item, expectedResult)
                && !tile.UseUrlFallback && !tile.IsSkeletonVisible && tile.DisplayedSource != null
                && ReferenceEquals(tile.DisplayedSource, QueueViewModel.Source.FirstOrDefault()?.LocalBitmapImage));
            Read();
        });
        Add("QueueSkeletonDark", "Dark", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Dark));
        Add("QueueSkeletonLight", "Light", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Light));
        Add("QueueLoadClose", "Close", (_, _) => PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow), 0x0010, IntPtr.Zero, IntPtr.Zero));
        QueueViewModel.Source.CollectionChanged += (_, _) =>
        {
            if (phase == "measuring" && !add.IsRunning) add.Start();
            if (QueueViewModel.Source.Count == Count && addMs < 0) addMs = add.ElapsedMilliseconds;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            var now = heartbeat.ElapsedMilliseconds;
            maxGap = Math.Max(maxGap, now - lastBeat);
            lastBeat = now;
            if (QueueViewModel.IsBulkAdding && frame.Content is Search search)
            {
                var bar = (ProgressBar)search.FindName("InfobarProgress");
                var textBlock = (TextBlock)search.FindName("InfoBarTextBlock");
                var text = string.Concat(textBlock.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text));
                var barPhase = text.StartsWith("Caching artwork") ? "artwork" : text.StartsWith("Adding to queue") ? "adding" : "";
                if (barPhase.Length > 0)
                {
                    if (barPhase == previousBarPhase && bar.Value + 0.01 < barValue) backwards++;
                    previousBarPhase = barPhase;
                    barValue = bar.Value;
                    indeterminate = bar.IsIndeterminate;
                }
            }
            Read();
        };
        timer.Start();
        var server = StartServer();
        App.MainWindow.Closed += (_, _) => { timer.Stop(); server.Close(); };
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
        App.MainWindow.AppWindow.Move(new Windows.Graphics.PointInt32(20, 20));
        Read();
    }

    private static HttpListener StartServer()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Unloaded.jpg"));
        var recognizableCover = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "FluentDL_logo.png"));
        var server = new HttpListener();
        server.Prefixes.Add(Origin);
        server.Start();
        _ = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                HttpListenerContext context;
                try { context = await server.GetContextAsync(); }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { break; }
                _ = Task.Run(async () =>
                {
                    var queueRequest = context.Request.Headers["X-FluentDL-Queue-Load-Test"] == "1";
                    if (queueRequest) Interlocked.Increment(ref requests);
                    else Interlocked.Increment(ref urlRequests);
                    if (context.Request.Url!.AbsolutePath == "/must-not-request.jpg") Interlocked.Increment(ref forbiddenRequests);
                    var current = queueRequest ? Interlocked.Increment(ref active) : active;
                    int previous;
                    do { previous = peak; if (previous >= current) break; } while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
                    try
                    {
                        await Task.Delay(delay);
                        if (failCover && context.Request.Url!.AbsolutePath == "/missing.jpg") context.Response.StatusCode = 503;
                        else
                        {
                            var recognizable = context.Request.Url!.AbsolutePath is "/lazy-ready.jpg" or "/converted.jpg";
                            var payload = recognizable ? recognizableCover : bytes;
                            context.Response.ContentType = recognizable ? "image/png" : "image/jpeg";
                            context.Response.Headers["Cache-Control"] = "no-store";
                            context.Response.ContentLength64 = payload.Length;
                            await context.Response.OutputStream.WriteAsync(payload);
                        }
                    }
                    catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException) { }
                    finally
                    {
                        if (queueRequest) Interlocked.Decrement(ref active);
                        try { context.Response.Close(); } catch (ObjectDisposedException) { }
                    }
                });
            }
        });
        return server;
    }
}
