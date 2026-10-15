using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// The Search page's preview pane for one fixed track or album per source, loaded by pasting its link: the title, the
/// detail rows, whether the popularity rating shows, and the album track count. Compared with a baseline recorded from
/// the pre-refactor build. Only runs with FLUENTDL_UI_TESTS=1. See SourceUiTests for setup.
/// Not covered: the cover image and the audio preview.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class PreviewPaneUiTests
{
    private static readonly (string Name, string Source, string Url, bool AlbumMode)[] Cases =
    [
        ("deezer-track", "deezer", $"https://www.deezer.com/track/{Fixtures.Tracks[0].DeezerId}", false),
        ("deezer-album", "deezer", $"https://www.deezer.com/album/{Fixtures.DeezerAlbumId}", true),
        ("qobuz-track", "qobuz", $"https://open.qobuz.com/track/{Fixtures.Tracks[0].QobuzId}", false),
        ("qobuz-album", "qobuz", $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}", true),
        ("spotify-track", "spotify", $"https://open.spotify.com/track/{Fixtures.Tracks[0].SpotifyId}", false),
        // No Spotify album: the dev app uses the web player, and album links there load nothing (see the live link tests).
        ("tidal-track", "tidal", $"https://tidal.com/track/{Fixtures.Tracks[0].TidalId}", false),
        ("tidal-album", "tidal", "https://tidal.com/album/20115556", true), // Random Access Memories
    ];

    // Separate, because a YouTube link crashes the app while YouTube bot-checks this machine (bug 20).
    private static readonly (string Name, string Source, string Url, bool AlbumMode)[] YoutubeCases =
    [
        ("youtube-track", "youtube", $"https://www.youtube.com/watch?v={Fixtures.Tracks[0].YoutubeId}", false),
    ];

    // These change from day to day, so only their presence is pinned.
    private static readonly HashSet<string> VolatileLabels = ["Fans", "Play Count", "Views"];

    [TestMethod]
    [Timeout(6 * 60 * 1000)]
    public void PreviewDetails() => Run("Ui.PreviewDetails", Cases);

    [TestMethod]
    [TestCategory("YouTube")]
    [Timeout(3 * 60 * 1000)]
    public void PreviewDetailsYoutube() => Run("Ui.PreviewDetailsYoutube", YoutubeCases);

    private static void Run(string baselineName, (string Name, string Source, string Url, bool AlbumMode)[] cases)
    {
        DevApp.RequireUiTests();
        DevApp.SeedTestAccounts();
        var state = new Dictionary<string, object?>();
        var shown = "";
        var lastRow = "";

        using (var dev = DevApp.Launch())
        {
            try
            {
                Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
                foreach (var c in cases)
                {
                    SetAlbumMode(dev, c.AlbumMode);
                    // The previous case's row stays until the new search clears the list, so wait for a different row
                    // from this source.
                    var before = lastRow;
                    bool NewRow() => ResultRows(dev) is [var row] && RowKey(row) is var key && key != before && key.EndsWith("|" + c.Source);
                    SubmitSearch(dev, c.Url);
                    try
                    {
                        WaitUntil(NewRow, TimeSpan.FromSeconds(15), $"the {c.Name} link result");
                    }
                    catch (TimeoutException)
                    {
                        // A submit right after the source picker closes is sometimes ignored.
                        SubmitSearch(dev, c.Url);
                        WaitUntil(NewRow, TimeSpan.FromSeconds(30), $"the {c.Name} link result (second try)");
                    }
                    var selected = ResultRows(dev)[0];
                    lastRow = RowKey(selected);
                    selected.Patterns.SelectionItem.Pattern.Select();
                    state[c.Name] = ReadPreview(dev, c.Name, ref shown);
                }
            }
            catch (Exception e)
            {
                var dir = SaveFailureEvidence(dev, "preview");
                throw new AssertFailedException($"{e.Message} Screenshot and UI tree saved to {dir}.", e);
            }
        }

        Snapshot.Match(baselineName, state);
    }

    private static AutomationElement[] ResultRows(DevApp dev) =>
        Find(dev, "CustomListView").FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));

    // A result row's title and source badge, such as "Get Lucky (...)|qobuz".
    private static string RowKey(AutomationElement row) =>
        Text(row) + "|" + (row.FindFirstDescendant(cf => cf.ByAutomationId("SourceBadge")) is { } badge ? Text(badge) : "");

    // The switch is in the source picker's flyout.
    private static void SetAlbumMode(DevApp dev, bool on)
    {
        SourceUiTests.OpenSourcePicker(dev);
        var toggle = Find(dev, "AlbumModeSwitch").Patterns.Toggle.Pattern;
        if ((toggle.ToggleState.Value == ToggleState.On) != on)
        {
            toggle.Toggle();
        }
        SourceUiTests.CloseSourcePicker(dev);
    }

    // Waits until the pane shows something other than the previous item and has stopped adding rows, then reads it.
    // The pane keeps the previous item's rows until the new ones arrive, and several items share a title.
    private static object ReadPreview(DevApp dev, string name, ref string previous)
    {
        string Snapshot() => string.Join("|", Labels(dev)) + "#" + string.Join("|", Values(dev)) + "#" + Text(Find(dev, "PreviewTitleText"));
        var before = previous;
        WaitUntil(() => Text(Find(dev, "PreviewTitleText")) != "" && Labels(dev).Length > 0 && Snapshot() != before, TimeSpan.FromSeconds(30), $"the {name} preview");
        previous = WaitForStable(Snapshot, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(45), $"the {name} preview");

        var labels = Labels(dev);
        var values = Values(dev);
        var rows = labels.Select((label, i) => new
        {
            Label = label,
            Value = VolatileLabels.Contains(label) ? "(varies)" : i < values.Length ? values[i] : "(missing)",
        }).ToArray();

        return new
        {
            Title = Text(Find(dev, "PreviewTitleText")),
            Rows = rows,
            LabelsMatchValues = labels.Length == values.Length,
            RatingShown = TryFind(dev, cf => cf.ByAutomationId("RankRatingControl")) != null,
            TrackListHeader = TryFind(dev, cf => cf.ByAutomationId("TrackListHeader")) is { } header ? Text(header) : null,
        };
    }

    // The pane's rows appear in the automation tree right after the title, flattened: every label, then every value.
    private static (string[] Labels, string[] Values) Rows(DevApp dev)
    {
        var title = Find(dev, "PreviewTitleText");
        var siblings = title.Parent.FindAllChildren();
        var texts = siblings.SkipWhile(e => !e.Equals(title)).Skip(1)
            .TakeWhile(e => e.Properties.ControlType.ValueOrDefault == ControlType.Text && e.Properties.AutomationId.ValueOrDefault != "TrackListHeader")
            .Select(Text).ToArray();
        var half = texts.Length / 2;
        return (texts[..half], texts[half..]);
    }

    private static string[] Labels(DevApp dev) => Rows(dev).Labels;

    private static string[] Values(DevApp dev) => Rows(dev).Values;
}
