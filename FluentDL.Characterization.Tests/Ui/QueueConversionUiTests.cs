using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// The queue's "Convert Sources" feature, end to end: tracks added from links, two conversions, the summary dialog
/// (tabs and their tracks) and each queue row's title, source and badge colour afterwards. Recorded from the
/// pre-refactor build (2cb77c1).
///
/// Spotify stays in web player mode, where converting to Spotify matches by metadata (Warning badges); converting to
/// Deezer matches by ISRC (Success badges). The Birdy track is a Deezer Session recording with no Spotify release,
/// so it fails (Error badge).
/// </summary>
[TestClass]
[TestCategory("UI")]
public class QueueConversionUiTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(3);

    private static readonly (string Source, string Url)[] Links =
    [
        ("deezer", $"https://www.deezer.com/track/{Fixtures.Tracks[2].DeezerId}"), // Beyoncé, Halo
        ("qobuz", $"https://open.qobuz.com/track/{Fixtures.Tracks[4].QobuzId}"), // Avicii, Levels
        ("spotify", $"https://open.spotify.com/track/{Fixtures.Tracks[3].SpotifyId}"), // YOASOBI, 夜に駆ける
        ("deezer", $"https://www.deezer.com/track/{Fixtures.Tracks[0].DeezerId}"), // Daft Punk, Get Lucky
        ("deezer", "https://www.deezer.com/track/1262003912"), // Birdy, Loneliness - Deezer Sessions (Women's Voices)
    ];

    [TestMethod]
    [Timeout(10 * 60 * 1000)]
    public void ConvertQueueTracks()
    {
        DevApp.RequireUiTests();
        DevApp.SeedTestAccounts();
        var state = new Dictionary<string, object?>();

        using var dev = DevApp.Launch();
        try
        {
            RunConversions(dev, state);
        }
        finally
        {
            // The queue is saved between runs, and SourceUiTests counts it, so leave it empty even after a failure.
            try { ClearQueue(dev); } catch (Exception) { }
        }

        Snapshot.Match("Ui.QueueConversion", state);
    }

    private static void RunConversions(DevApp dev, Dictionary<string, object?> state)
    {
        RunStep(dev, "fill-queue", () =>
        {
            Find(dev, "SearchBox", StartupTimeout);
            ClearQueue(dev); // the queue is saved between runs
            Navigate(dev, "Search");
            foreach (var (source, url) in Links)
            {
                AddLinkToQueue(dev, source, url);
            }

            Navigate(dev, "Queue");
            WaitUntil(() => Text(Find(dev, "CountText")) == $"{Links.Length} tracks in queue", TimeSpan.FromSeconds(30), "the queue to fill");
            state["Before"] = ReadQueueRows(dev);
        });

        RunStep(dev, "convert-to-deezer", () =>
        {
            Convert(dev, inputs: ["Qobuz", "Spotify"], output: "Deezer");
            state["ToDeezerSummary"] = ReadSummary(dev);
            state["AfterToDeezer"] = ReadQueueRows(dev);
        });

        RunStep(dev, "convert-to-spotify", () =>
        {
            Convert(dev, inputs: ["Deezer"], output: "Spotify");
            state["ToSpotifySummary"] = ReadSummary(dev);
            state["AfterToSpotify"] = ReadQueueRows(dev);
        });
    }

    private static void RunStep(DevApp dev, string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            var dir = SaveFailureEvidence(dev, name);
            throw new AssertFailedException($"{e.Message} Screenshot and UI tree saved to {dir}.", e);
        }
    }

    // The clear dialog starts with every source checked.
    private static void ClearQueue(DevApp dev)
    {
        Navigate(dev, "Queue");
        var count = Find(dev, "CountText");
        if (Text(count) == "No tracks in queue") return;

        Invoke(Find(dev, "ClearButton"));
        WaitUntil(() => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Length > 0, DefaultTimeout, "the clear dialog's checkboxes");
        Invoke(Find(dev, "PrimaryButton"));
        WaitUntil(() => Text(Find(dev, "CountText")) == "No tracks in queue", TimeSpan.FromSeconds(30), "the queue to clear");
    }

    // A track link loads one result; wait for it before adding. The previous link's row stays until replaced, and two
    // links in a row can be from the same source, so wait for the row itself to change: adding too early added the
    // previous track again, which the queue ignores as a duplicate.
    // Spotify web player links fail now and then without a message, so a link gets a second try.
    private static void AddLinkToQueue(DevApp dev, string source, string url)
    {
        string[] VisibleBadges() => Find(dev, "CustomListView")
            .FindAllDescendants(cf => cf.ByAutomationId("SourceBadge")).Select(Text).ToArray();
        string? FirstRow() => Find(dev, "CustomListView").FindFirstChild(cf => cf.ByControlType(ControlType.ListItem)) is { } row ? Text(row) : null;
        var previousRow = FirstRow();
        bool Loaded() => VisibleBadges() is [var only] && only == source && Text(Find(dev, "ResultsText")) == "Add 1 to queue"
            && FirstRow() != previousRow;

        SubmitSearch(dev, url);
        try
        {
            WaitUntil(Loaded, TimeSpan.FromSeconds(45), $"the {source} link {url} to load");
        }
        catch (TimeoutException)
        {
            SubmitSearch(dev, url);
            WaitUntil(Loaded, TimeSpan.FromSeconds(45), $"the {source} link {url} to load (second try)");
        }
        Invoke(Find(dev, "AddToQueueButton"));
        WaitUntil(() => Find(dev, "AddToQueueButton").IsEnabled, TimeSpan.FromSeconds(30), "adding the track to the queue");
    }

    private static void Convert(DevApp dev, string[] inputs, string output)
    {
        Invoke(Find(dev, "ConvertDialogOpenButton"));
        Find(dev, "OutputComboBox");
        WaitUntil(() => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Length > 0, DefaultTimeout, "the conversion dialog's checkboxes");

        foreach (var box in FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Select(c => c.AsCheckBox()))
        {
            var wanted = inputs.Contains(Text(box));
            if (box.IsEnabled && box.IsChecked != wanted)
            {
                box.Patterns.Toggle.Pattern.Toggle();
            }
        }

        var combo = Find(dev, "OutputComboBox").AsComboBox();
        combo.Expand();
        WaitUntil(() => combo.Items.Any(i => i.Text == output), DefaultTimeout, $"'{output}' in the output list");
        combo.Items.First(i => i.Text == output).Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();

        Invoke(Find(dev, "PrimaryButton"));
        Find(dev, "ConversionTabView", ConversionTimeout);
    }

    // Each tab of the "Conversion Summary" dialog and the titles it lists, sorted: the list is in the order the parallel
    // conversions finished.
    private static object ReadSummary(DevApp dev)
    {
        var tabs = new Dictionary<string, string[]>();
        foreach (var tab in new[] { "SuccessTab", "WarningTab", "ErrorTab" })
        {
            Find(dev, tab).Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(500); // the list's items source changes on selection
            tabs[Text(Find(dev, tab))] = Find(dev, "ConversionListView")
                .FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Select(Text).Order(StringComparer.Ordinal).ToArray();
        }

        CloseDialog(dev);
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("ConversionTabView")) == null, DefaultTimeout, "the summary to close");
        return tabs;
    }

    // The badge colours QueuePage gives converted rows. The badge has no automation peer, so it is read from the screen:
    // it is centred on the top-left corner of the row's content grid.
    private static readonly (string Name, Color Color)[] BadgeColors =
    [
        ("success", Color.FromArgb(108, 203, 95)),
        ("warning", Color.FromArgb(252, 225, 0)),
        ("error", Color.FromArgb(255, 153, 164)),
        ("exception", Color.FromArgb(255, 0, 0)),
    ];

    private static object ReadQueueRows(DevApp dev)
    {
        Thread.Sleep(1000); // the last row's badge is set after the summary opens
        var rows = Find(dev, "CustomListView").FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
        return rows.Select(row =>
        {
            var content = row.FindFirstChild() ?? row;
            var texts = content.FindAllChildren(cf => cf.ByAutomationId("Segment1")).Select(Text).ToArray();
            return new
            {
                Title = texts.ElementAtOrDefault(0),
                Artists = texts.ElementAtOrDefault(1),
                Source = Text(content.FindFirstChild(cf => cf.ByAutomationId("SourceBadge"))),
                Badge = ReadBadge(content.BoundingRectangle),
            };
        }).ToArray();
    }

    // The 12 px badge is centred near the content's top-left corner (about 4 px in). A colour counts when at least 20
    // pixels of a 16 px box there are close to it, so a few similar pixels in a cover image don't.
    private static string ReadBadge(Rectangle content)
    {
        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(new Rectangle(content.Left - 4, content.Top - 4, 16, 16));
        var bitmap = capture.Bitmap;
        var counts = BadgeColors.ToDictionary(c => c.Name, _ => 0);
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                foreach (var (name, color) in BadgeColors)
                {
                    var distance = Math.Sqrt(Math.Pow(color.R - pixel.R, 2) + Math.Pow(color.G - pixel.G, 2) + Math.Pow(color.B - pixel.B, 2));
                    if (distance < 30) counts[name]++;
                }
            }
        }
        var best = counts.MaxBy(c => c.Value);
        return best.Value >= 20 ? best.Key : "none";
    }
}