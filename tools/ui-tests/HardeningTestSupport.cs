using System.Text.Json;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Core.Updates;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FluentDL;

// Only HardeningPreview.targets includes this source. It never uses real provider accounts or an installer.
internal static class HardeningTestSupport
{
    private static readonly Updates updater = new();
    private static Task? writerTask;
    private static string Folder => ApplicationData.Current.LocalFolder.Path;
    internal static void Register(IServiceCollection services) => services.AddSingleton(new SourceRegistry(new ISource[] { new Source() }));

    private static void CheckFixture()
    {
        if (Environment.UserName != "WDAGUtilityAccount"
            || Windows.ApplicationModel.Package.Current.Id.Name != "DerekYang2.FluentDL.V4HardeningTest"
            || !File.Exists(Path.Combine(Folder, "hardening-fixture.marker")))
            throw new InvalidOperationException("The isolated Sandbox hardening fixture is missing.");
    }

    internal static void Attach(UIElement? content)
    {
        CheckFixture();
        var shell = (ShellPage)content!;
        var root = (Grid)shell.Content;
        var frame = (Frame)shell.FindName("NavigationFrame");
        root.RowDefinitions.Add(new() { Height = new GridLength(48) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        foreach (FrameworkElement child in root.Children) Grid.SetRow(child, child.Name == "AppTitleBar" ? 0 : 2);
        var toolbar = new StackPanel { Spacing = 8, Margin = new Thickness(16, 8, 16, 8) };
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(buttons);
        var status = new TextBlock { Text = "ready", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "HardeningStatus");
        toolbar.Children.Add(status);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(notice, "HardeningNotice");
        toolbar.Children.Add(notice);
        const string name = "x</a><a href='x'>y & z";
        UrlParser.ParseTextBlock(notice, StatusMarkup.Format($"<b>Fixture</b> Loaded <a href='https://example.test/playlist'>{name}</a>"));
        void Button(string id, string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) => await UiOperation.RunAsync(action, error => status.Text = "failed: " + error.Message);
            buttons.Children.Add(button);
        }
        Button("HardeningQueue", "Open queue", () =>
        {
            App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            return Task.CompletedTask;
        });
        Button("HardeningSnapshot", "Snapshot and inspect", async () =>
        {
            status.Text = "capturing";
            await QueueViewModel.SaveForUpdateAsync(CancellationToken.None);
            await CaptureAsync();
            status.Text = "captured " + QueueViewModel.Source.Count + " rows";
        });
        Button("HardeningAdd", "Admit a track", async () =>
        {
            QueueViewModel.Add(new SongSearchObject { Source = SourceIds.Deezer, Id = "admitted", Title = "Admitted after provider failure", Artists = "Fixture", Duration = "1" });
            await DatabaseService.FlushAsync();
            status.Text = "admitted";
        });
        Button("HardeningPreview", "Fail preview", async () =>
        {
            if (frame.Content is not QueuePage) App.GetService<INavigationService>().NavigateTo(typeof(QueueViewModel).FullName!);
            var page = (QueuePage)frame.Content;
            var preview = (PreviewPane)page.FindName("PreviewPanel");
            preview.Show();
            await preview.Update(new SongSearchObject { Source = SourceIds.Deezer, Id = "preview", Title = "Failed provider preview", Duration = "1" });
            status.Text = "preview handled";
        });
        var updateModel = new AppUpdateViewModel(updater);
        CancellationTokenSource? writerCancellation = null;
        ManualResetEventSlim? writerRelease = null;
        Button("HardeningWriter", "Pause a file writer", async () =>
        {
            if (writerTask is { IsCompleted: false }) throw new InvalidOperationException("The synthetic writer is already active.");
            writerCancellation = new();
            writerRelease = new();
            var release = writerRelease;
            writerTask = ApplicationActivity.RunFileWriteAsync(() =>
            {
                if (!release.Wait(TimeSpan.FromMinutes(2))) throw new TimeoutException("The synthetic writer was not released.");
                File.WriteAllText(Path.Combine(Folder, "hardening-writer-completed"), "Synthetic file write completed");
            });
            status.Text = "writer paused";
            try { await writerTask.WaitAsync(writerCancellation.Token); }
            catch (OperationCanceledException) { status.Text = "caller cancelled; writer still active"; }
        });
        Button("HardeningCancelWriter", "Cancel the caller", () => { writerCancellation?.Cancel(); return Task.CompletedTask; });
        Button("HardeningReleaseWriter", "Finish the writer", async () =>
        {
            writerRelease?.Set();
            if (writerTask != null) await writerTask;
            writerRelease?.Dispose();
            writerCancellation?.Dispose();
            status.Text = "writer finished";
        });
        Button("HardeningInstall", "Try a fake update", async () =>
        {
            if (!updateModel.IsDownloaded) { await updateModel.CheckAsync(); await updateModel.DownloadAsync(); }
            await updateModel.InstallAsync(ApplicationActivity.Gate, _ =>
            {
                File.WriteAllText(Path.Combine(Folder, "hardening-install-prepared"), "Synthetic preparation only");
                return Task.CompletedTask;
            });
            status.Text = updater.Installs == 0 ? "blocked: " + updateModel.Message : "fake installation admitted";
        });
    }

    private static async Task CaptureAsync()
    {
        var saved = await DatabaseService.LoadQueueJSON();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Folder, "fluentdl.db"), Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Image FROM Items WHERE Hash='localZ:\\fixture-missing\\second.flac'";
        var artwork = await command.ExecuteScalarAsync() as byte[];
        await File.WriteAllTextAsync(Path.Combine(Folder, "hardening-state.json"), JsonSerializer.Serialize(new
        {
            Rows = QueueViewModel.Source.Select(row => new { row.Source, row.Id, row.Title, row.QueueCounter, row.QueueSubOrder, row.AlbumTracksCount }).ToArray(),
            Saved = saved, ArtworkLength = artwork?.Length ?? 0,
            ArtworkHash = artwork != null ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(artwork)) : null,
            WriterPending = writerTask is { IsCompleted: false }, FakeInstalls = updater.Installs,
            InstallPrepared = File.Exists(Path.Combine(Folder, "hardening-install-prepared")),
        }));
    }

    private sealed class Updates : IAppUpdateService
    {
        internal int Installs;
        public string InstallationDescription => "Synthetic hardening updater";
        public Uri HelpUri => new("https://example.test/");
        public string HelpLabel => "Synthetic updater";
        public Task<AppUpdateOffer?> CheckAsync(CancellationToken token) => Task.FromResult<AppUpdateOffer?>(new("Synthetic update"));
        public Task DownloadAsync(AppUpdateOffer offer, IProgress<double> progress, CancellationToken token) => Task.CompletedTask;
        public Task InstallAsync(AppUpdateOffer offer, CancellationToken token) { Installs++; return Task.CompletedTask; }
    }

    private sealed class Source : ISourceSession, ICatalogSource, IDownloadSource, IPreviewSource
    {
        public SourceDescriptor Descriptor => SourceCatalog.Find(SourceIds.Deezer)!;
        public bool IsConnected => true;
        public string? StatusText => "Synthetic hardening provider";
        public async Task<ConnectResult> ConnectAsync()
        {
            CheckFixture();
            var saved = await DatabaseService.LoadQueueJSON();
            // This call fails if provider startup ran before local persistence initialized.
            const string probe = "localZ:\\fixture-missing\\first.flac";
            await DatabaseService.SaveMetadataAsync(probe, saved[probe]);
            await File.WriteAllTextAsync(Path.Combine(Folder, "hardening-startup.json"), JsonSerializer.Serialize(new
            {
                PersistenceReady = true, Restored = QueueViewModel.Source.Count, Saved = saved.Count,
            }));
            if (File.ReadAllText(Path.Combine(Folder, "hardening-fixture.marker")).Trim() == "stall")
                await Task.Delay(TimeSpan.FromSeconds(40));
            throw new HttpRequestException("Synthetic provider startup failure");
        }
        public Task<SongSearchObject?> GetTrackAsync(string id) => Task.FromResult<SongSearchObject?>(Track(id));
        public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => Task.FromResult(new List<SongSearchObject> { Track("one"), Track("two"), Track("three") });
        public Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) => Task.FromResult<string?>(null);
        public Task<PreviewInfo> GetPreviewAsync(SongSearchObject song) => Task.FromException<PreviewInfo>(new HttpRequestException("Synthetic metadata preview failure"));
        public Task<string?> GetPreviewAudioAsync(SongSearchObject track) => Task.FromException<string?>(new HttpRequestException("Synthetic audio preview failure"));
        public async Task<string> DownloadAsync(SongSearchObject track, string pathWithoutExtension, DownloadRequest request, IProgress<ProgressData>? progress)
        {
            if (track.Id == "three") throw new HttpRequestException("Synthetic track download failure");
            var path = pathWithoutExtension + (track.Id == "one" ? ".wav" : ".flac");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (track.Id == "one")
            {
                using var file = File.Create(path);
                using var writer = new BinaryWriter(file);
                const int samples = 44100;
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(samples); writer.Write(samples * 2);
                writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
                for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / samples) * 4096));
            }
            else await File.WriteAllTextAsync(path, "Synthetic unparseable audio");
            return path;
        }
        private static SongSearchObject Track(string id) => new() { Source = SourceIds.Deezer, Id = id, Title = "Album " + id, Artists = "Fixture", Duration = "1", TrackPosition = id == "one" ? "1" : id == "two" ? "2" : "3" };
    }
}
