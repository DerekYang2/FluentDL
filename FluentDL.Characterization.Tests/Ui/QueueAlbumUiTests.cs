using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// An album in the queue, end to end: added from Search as one row, kept as an album across a restart, then downloaded
/// with "Download all", which replaces the row with the album's local tracks in order. The Qobuz test account
/// downloads 30-second samples, so this takes about a minute.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class QueueAlbumUiTests
{
    private static readonly string[] TouchedKeys =
    [
        "download_directory", "ask_before_download", "album_subfolders", "subfolders", "download_lyrics",
        "deezer_arl", "qobuz_id", "qobuz_token", "qobuz_app_id", "qobuz_app_secret", "tidal_client_id", "tidal_client_secret", "tidal_country",
    ];

    [TestMethod]
    [Timeout(10 * 60 * 1000)]
    public void AlbumStaysOneRowThenDownloadsIntoItsTracks()
    {
        DevApp.RequireUiTests();
        var data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(DevApp.FindAumid().Split('!')[0]);
        var previous = TouchedKeys.ToDictionary(k => k, k => data.LocalSettings.Values.TryGetValue(k, out var value) ? value : null);
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "queue-album-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        DevApp.SeedTestAccounts();
        data.LocalSettings.Values["download_directory"] = Newtonsoft.Json.JsonConvert.SerializeObject(folder);
        data.LocalSettings.Values["ask_before_download"] = "false";
        data.LocalSettings.Values["album_subfolders"] = "true";
        data.LocalSettings.Values["subfolders"] = "false";
        data.LocalSettings.Values["download_lyrics"] = "false";
        try
        {
            using (var dev = DevApp.Launch())
            {
                Step(dev, "add-album", () =>
                {
                    Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
                    QueueConversionUiTests.ClearQueue(dev);
                    Navigate(dev, "Search");
                    SetAlbumMode(dev, true);
                    SubmitSearch(dev, $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}");
                    WaitUntil(() => Text(Find(dev, "ResultsText")) == "Add 1 to queue", TimeSpan.FromSeconds(45), "the album link to load");
                    Invoke(Find(dev, "AddToQueueButton"));
                    WaitUntil(() => Find(dev, "AddToQueueButton").IsEnabled, TimeSpan.FromSeconds(30), "adding the album");
                    SetAlbumMode(dev, false);
                    Navigate(dev, "Queue");
                    WaitUntil(() => Text(Find(dev, "CountText")) == "1 album", TimeSpan.FromSeconds(15), "one album row");
                    StringAssert.Contains(RowTexts(dev).Single(), "13 Tracks");
                });
            }

            using (var dev = DevApp.Launch())
            {
                Step(dev, "download-album", () =>
                {
                    Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
                    Navigate(dev, "Queue");
                    WaitUntil(() => Text(Find(dev, "CountText")) == "1 album", TimeSpan.FromSeconds(15), "the album row after a restart");

                    Invoke(Find(dev, "DownloadAllButton"));
                    Find(dev, "ConversionTabView", TimeSpan.FromMinutes(5));
                    CloseDialog(dev);
                    WaitUntil(() => Text(Find(dev, "CountText")) == "13 tracks", TimeSpan.FromSeconds(30), "the album to expand into its tracks");
                    var badges = Find(dev, "CustomListView").FindAllDescendants(cf => cf.ByAutomationId("SourceBadge")).Select(Text).Distinct().ToArray();
                    CollectionAssert.AreEqual(new[] { "local" }, badges, "Every visible row should be a local file.");
                    Assert.AreEqual(13, Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Count(f => f.EndsWith(".flac") || f.EndsWith(".mp3")));
                    var first = RowTexts(dev).First();
                    Console.WriteLine("First expanded row: " + first);
                    StringAssert.Contains(first, "Give Life Back to Music", "The tracks keep album order.");
                });
                Step(dev, "cleanup", () => QueueConversionUiTests.ClearQueue(dev));
            }
        }
        finally
        {
            foreach (var (key, value) in previous)
                if (value == null) data.LocalSettings.Values.Remove(key); else data.LocalSettings.Values[key] = value;
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static string[] RowTexts(DevApp dev) => Find(dev, "CustomListView")
        .FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
        .Select(row => string.Join(" | ", row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(Text)))
        .ToArray();

    private static void SetAlbumMode(DevApp dev, bool on)
    {
        SourceUiTests.OpenSourcePicker(dev);
        var toggle = Find(dev, "AlbumModeSwitch").Patterns.Toggle.Pattern;
        if ((toggle.ToggleState.Value == ToggleState.On) != on) toggle.Toggle();
        SourceUiTests.CloseSourcePicker(dev);
    }

    private static void Step(DevApp dev, string name, Action step)
    {
        try { step(); }
        catch (Exception e)
        {
            var dir = SaveFailureEvidence(dev, "queue-album-" + name);
            throw new AssertFailedException($"{e.Message} Screenshot and UI tree saved to {dir}.", e);
        }
    }
}
