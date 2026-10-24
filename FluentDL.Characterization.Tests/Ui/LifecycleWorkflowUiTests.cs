using System.IO.Compression;
using FlaUI.Core.AutomationElements;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using static FluentDL.Characterization.Tests.Ui.QueueWorkflowSteps;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>Qobuz represents shared application behavior. These are not repeated for every source.</summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("Workflow")]
[DoNotParallelize]
public class LifecycleWorkflowUiTests
{
    [TestMethod]
    [TestCategory("Deferred")]
    [Timeout(4 * 60 * 1000)]
    public void MixedQueueExportsImportsAndRestartsWithoutLosingOrderOrKind()
    {
        RequireSandbox();
        using var fixture = new DownloadProfile();
        var archive = Path.Combine(fixture.Folder, "exported-queue.zip");
        SavedRow[] expected = [];
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "export-add-track-and-album", () =>
            {
                AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/track/{Fixtures.Tracks[2].QobuzId}", false);
                AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", true);
                expected = WaitSaved(2);
                Assert.IsFalse(expected[0].Album);
                Assert.IsTrue(expected[1].Album);
                Navigate(dev, "Queue");
                Invoke(Find(dev, "RestoreQueueButton"));
                Invoke(Find(dev, "SecondaryButton")); // Export
                ChooseFile(dev, archive);
                WaitUntil(() => File.Exists(archive) && new FileInfo(archive).Length > 0, DefaultTimeout, "the exported ZIP");
                using var zip = ZipFile.OpenRead(archive);
                Assert.IsTrue(zip.Entries.Any(e => e.Name.EndsWith(".db")), "The export contains a queue database.");
            });
            Step(dev, "import-exported-queue", () =>
            {
                QueueConversionUiTests.ClearQueue(dev);
                Invoke(Find(dev, "RestoreQueueButton"));
                Invoke(Find(dev, "ImportBackupButton"));
                ChooseFile(dev, archive);
                WaitUntil(() => Find(dev, "RestoreQueueBackupComboBox").AsComboBox().SelectedItem?.Text.Contains("exported-queue") == true,
                    DefaultTimeout, "the imported file to be selected");
                Invoke(Find(dev, "PrimaryButton"));
                WaitUntil(() => Text(Find(dev, "CountText")) == Views.QueuePage.FormatQueueCount(1, 1), DefaultTimeout, "the exported queue to restore");
                CollectionAssert.AreEqual(expected, WaitSaved(2));
            });
            PreviewEveryRow(dev, expected, "export-imported");
        }
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            CollectionAssert.AreEqual(expected, WaitSaved(2));
            PreviewEveryRow(dev, expected, "export-restarted");
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    [TestMethod]
    [Timeout(4 * 60 * 1000)]
    public void StopLeavesCompletedTracksIntactAndRemainingTracksCanBeRetried()
    {
        RequireSandbox();
        using var fixture = new DownloadProfile(threads: 1);
        var completed = new Dictionary<string, byte[]>();
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "stop-queue-download", () =>
            {
                foreach (var track in Fixtures.Tracks.Take(6))
                    AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/track/{track.QobuzId}", false);
                WaitSaved(6);
                Navigate(dev, "Queue");
                Invoke(Find(dev, "DownloadAllButton"));
                WaitUntil(() => SavedRows().Any(r => r.Source == SourceIds.Local), DefaultTimeout, "one completed download before Stop");
                foreach (var row in SavedRows().Where(r => r.Source == SourceIds.Local))
                    completed[row.Id] = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(row.Id));
                Invoke(Find(dev, "ConvertStopButton"));
                CloseSummary(dev);
                Assert.IsTrue(Find(dev, "DownloadAllButton").IsEnabled);
                Assert.IsTrue(Find(dev, "ConvertDialogOpenButton").IsEnabled);
                var stopped = WaitSaved(6);
                Assert.IsTrue(stopped.Any(r => r.Source == SourceIds.Qobuz), "Stop must leave some work unstarted.");
                Assert.IsTrue(completed.Keys.All(path => stopped.Any(r => r.Id == path)), "Completed rows survive Stop.");
                dev.AssertHealthy();
            });
        }
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            Step(dev, "retry-stopped-download", () =>
            {
                Invoke(Find(dev, "DownloadAllButton"));
                CloseSummary(dev);
                WaitUntil(() => SavedRows() is { Length: 6 } rows && rows.All(r => r.Source == SourceIds.Local),
                    DefaultTimeout, "all six tracks to become local after retry");
                Assert.AreEqual(6, Directory.GetFiles(fixture.Folder, "*.mp3").Length);
                Assert.AreEqual(6, SavedRows().Select(r => r.Id).Distinct().Count());
                foreach (var (path, hash) in completed)
                    CollectionAssert.AreEqual(hash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)), "Retry must not alter a completed file.");
                Assert.IsTrue(Find(dev, "DownloadAllButton").IsEnabled);
                Capture(dev, "stop-retried.png");
            });
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    [TestMethod]
    [Timeout(5 * 60 * 1000)]
    public void OneBlockedAlbumFileLeavesOneRetryableRowAndDoesNotLoseSuccessfulTracks()
    {
        RequireSandbox();
        using var fixture = new DownloadProfile();
        var blocked = Path.Combine(fixture.Folder, Fixtures.Tracks[0].QobuzId + ".mp3");
        const string sentinel = "This existing file must not be overwritten.";
        File.WriteAllText(blocked, sentinel);
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            QueueConversionUiTests.ClearQueue(dev);
            Step(dev, "partial-album-file-collision", () =>
            {
                AddLink(dev, SourceIds.Qobuz, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", true);
                WaitSaved(1);
                Navigate(dev, "Queue");
                Invoke(Find(dev, "DownloadAllButton"));
                Find(dev, "ConversionTabView", TimeSpan.FromMinutes(2));
                Find(dev, "ErrorTab").Patterns.SelectionItem.Pattern.Select();
                WaitUntil(() => Find(dev, "ConversionListView").FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length == 1,
                    DefaultTimeout, "one failed track in the summary");
                CloseDialog(dev);
                var rows = WaitSaved(13);
                Assert.AreEqual(12, rows.Count(r => r.Source == SourceIds.Local));
                var retry = rows.Single(r => r.Source == SourceIds.Qobuz);
                Assert.AreEqual(Fixtures.Tracks[0].QobuzId, retry.Id);
                Assert.IsFalse(rows.Any(r => r.Album));
                Assert.AreEqual(sentinel, File.ReadAllText(blocked));
                Capture(dev, "partial-album.png");
            });
        }
        File.Delete(blocked); // remove the controlled failure, not any successful file
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Queue");
            Assert.AreEqual(12, WaitSaved(13).Count(r => r.Source == SourceIds.Local));
            Step(dev, "retry-only-the-failed-track", () =>
            {
                Invoke(Find(dev, "DownloadAllButton"));
                CloseSummary(dev);
                WaitUntil(() => SavedRows() is { Length: 13 } rows && rows.All(r => r.Source == SourceIds.Local),
                    DefaultTimeout, "the failed track to become local");
                Assert.AreEqual(13, Directory.GetFiles(fixture.Folder, "*.mp3").Length);
                Assert.AreEqual(13, SavedRows().Select(r => r.Id).Distinct().Count());
                Capture(dev, "partial-album-retried.png");
            });
            QueueConversionUiTests.ClearQueue(dev);
        }
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 13)]
    [Timeout(4 * 60 * 1000)]
    public void SearchDownloadFinishesAndAddsFilesToLocalExplorer(bool album, int count)
    {
        RequireSandbox();
        using var fixture = new DownloadProfile();
        using var dev = DevApp.Launch();
        Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
        Step(dev, album ? "search-download-album" : "search-download-track", () =>
        {
            var url = album ? $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}" : $"https://open.qobuz.com/track/{Fixtures.Tracks[0].QobuzId}";
            LoadLink(dev, SourceIds.Qobuz, url, album);
            Invoke(Find(dev, "DownloadButton"));
            WaitUntil(() => Text(Find(dev, "InfoBarTextBlock")).Contains("Successfully downloaded"),
                TimeSpan.FromMinutes(2), "the Search download result");
            Assert.AreEqual(count, Directory.GetFiles(fixture.Folder, "*.mp3").Length);
            Assert.IsTrue(TryFind(dev, cf => cf.ByAutomationId("InfobarProgress")) is not { IsOffscreen: false }, "The download progress must be hidden at completion.");
            Navigate(dev, "Local");
            WaitUntil(() => Find(dev, "FileListView").FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length > 0,
                DefaultTimeout, "the downloaded files in Local Explorer");
            WaitUntil(() => Text(Find(dev, "ResultsText")) == $"Add {count} to queue", DefaultTimeout,
                "the Local Explorer count on its first visit after downloading");
            Assert.IsTrue(Find(dev, "ClearButton").IsEnabled);
            Capture(dev, album ? "search-album-local-explorer.png" : "search-track-local-explorer.png");
            Navigate(dev, "Search");
            Navigate(dev, "Local");
            Assert.AreEqual($"Add {count} to queue", Text(Find(dev, "ResultsText")), "Navigating back keeps the count.");
            Invoke(Find(dev, "ClearButton"));
            WaitUntil(() => Text(Find(dev, "ResultsText")) == "No results" && !Find(dev, "ClearButton").IsEnabled,
                DefaultTimeout, "the empty-list state after Clear");
            Assert.AreEqual(count, Directory.GetFiles(fixture.Folder, "*.mp3").Length, "Clearing the view must not delete downloaded files.");
        });
    }

    private static void CloseSummary(DevApp dev)
    {
        Find(dev, "ConversionTabView", TimeSpan.FromMinutes(2));
        // UIA can expose the dialog during its opening animation, before the close button accepts Invoke.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var button = Find(dev, "CloseButton");
            WaitUntil(() => button.IsEnabled && !button.IsOffscreen, DefaultTimeout, "the summary close button to be ready");
            Invoke(button);
            Thread.Sleep(500);
            if (TryFind(dev, cf => cf.ByAutomationId("ConversionTabView")) == null) return;
        }
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("ConversionTabView")) == null, DefaultTimeout, "the summary to close");
    }

    private sealed class DownloadProfile : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "opencode", "lifecycle-" + Guid.NewGuid());
        private readonly CatalogWorkflowUiTests.Profile settings;

        public DownloadProfile(int threads = 3)
        {
            Directory.CreateDirectory(Folder);
            settings = new CatalogWorkflowUiTests.Profile(overrides: new Dictionary<string, object?>
            {
                ["download_directory"] = Folder, ["ask_before_download"] = false, ["file_naming_wildcard"] = "{id}",
                ["album_subfolders"] = false, ["subfolders"] = false, ["overwrite"] = false, ["qobuz_quality"] = 0,
                ["conversion_threads"] = threads, ["download_lyrics"] = false, ["download_album_cover_art"] = false,
                ["replaygain_auto_apply"] = false, ["replaygain_album_downloads"] = false, ["notifications"] = false,
                ["queue_download_checked"] = true,
            });
        }

        public void Dispose()
        {
            settings.Dispose();
            Directory.Delete(Folder, true);
        }
    }
}
