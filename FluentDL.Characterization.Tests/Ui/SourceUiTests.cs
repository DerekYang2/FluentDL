using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// Drives the "FluentDL (Dev)" package through the parts of the UI that Phase 2 (source registry) changes:
/// the search source picker, a search on each source, the result source badges, the queue's conversion and clear
/// dialogs, and the saved search source after a restart. The result is compared with a baseline recorded from the
/// pre-refactor build.
///
/// Only runs with FLUENTDL_UI_TESTS=1, because it drives a real window. Install the dev package first with
/// tools/ui-tests/Install-DevPackage.ps1; it starts with empty settings, which this flow relies on.
/// On failure, a screenshot and the UI Automation tree go to %TEMP%\opencode\fluentdl-ui-failures.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SourceUiTests
{
    private static readonly string[] Sources = ["Deezer", "Qobuz", "Spotify", "YouTube"];
    private const string Query = "daft punk get lucky";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60); // startup waits up to 30 s for the services
    private static readonly TimeSpan ResultsTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    [Timeout(8 * 60 * 1000)]
    public void SourcePickerSearchAndQueueDialogs()
    {
        DevApp.RequireUiTests();
        var state = new Dictionary<string, object?>();

        // Qobuz search needs a signed-in user, so sign the dev app in with the values from the secrets file.
        // Spotify stays in web player mode; the characterization tests cover the official API.
        DevApp.SeedTestAccounts();

        using (var dev = DevApp.Launch())
        {
            RunStep(dev, "first-session", () =>
            {
                Find(dev, "SearchBox", StartupTimeout);

                // This test checks a fresh queue and the default picker, even when -SkipInstall preserves a
                // previous test's queue and selected source. Only the separate dev package is affected.
                QueueConversionUiTests.ClearQueue(dev);
                Navigate(dev, "Search");
                SelectSource(dev, "Deezer");

                state["NavigationItems"] = dev.MainWindow
                    .FindAllDescendants(cf => cf.ByClassName("Microsoft.UI.Xaml.Controls.NavigationViewItem"))
                    .Select(Text).ToArray();

                state["SourcePickerOnFirstStart"] = ReadSourcePicker(dev);

                var searches = new List<object>();
                var added = 0;
                foreach (var source in Sources)
                {
                    SelectSource(dev, source);
                    var (summary, count) = Search(dev, source);
                    searches.Add(summary);

                    if (source is "Deezer" or "YouTube")
                    {
                        Invoke(Find(dev, "AddToQueueButton"));
                        WaitUntil(() => Find(dev, "AddToQueueButton").IsEnabled, TimeSpan.FromSeconds(45), "adding results to the queue");
                        Console.WriteLine($"{source}: {count} search results; " + Text(Find(dev, "InfoBarTextBlock")));
                        added += count;
                    }
                }
                state["Searches"] = searches;

                Navigate(dev, "Queue");
                var queueText = WaitForStable(() => Text(Find(dev, "CountText")), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20), "queue count");
                // YouTube's count varies (see below), so check the queue against what was added instead of a fixed number.
                state["QueueCountEqualsAddedResults"] = queueText == Views.QueuePage.FormatQueueCount(added, 0);
                Console.WriteLine($"Expected queue: {added}; actual: {queueText}");
                state["ConversionDialog"] = ReadConversionDialog(dev);
                state["ClearDialog"] = ReadClearDialog(dev);
            });
        }

        // The source picked last (YouTube) is saved when the picker closes and restored on the next start.
        using (var dev = DevApp.Launch())
        {
            RunStep(dev, "after-restart", () =>
            {
                Find(dev, "SearchBox", StartupTimeout);
                state["SourcePickerAfterRestart"] = ReadSourcePicker(dev);
            });
        }

        Snapshot.Match("Ui.SourcePickerSearchAndQueueDialogs", state);
    }

    // Saves a screenshot and the UI tree when a step fails, then rethrows.
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

    internal static AutomationElement OpenSourcePicker(DevApp dev)
    {
        var button = Find(dev, "SourceButton");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var expandCollapse = button.Patterns.ExpandCollapse.Pattern;
            // Right after closing, the button can still report Expanded, and Expand is then ignored.
            if (expandCollapse.ExpandCollapseState.Value == ExpandCollapseState.Expanded)
            {
                expandCollapse.Collapse();
                Thread.Sleep(300 * attempt);
            }

            expandCollapse.Expand();

            try
            {
                return Find(dev, "SourceRadioButtons", TimeSpan.FromSeconds(4));
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
        }
        throw new InvalidOperationException("The source picker did not open.");
    }

    internal static void CloseSourcePicker(DevApp dev)
    {
        Find(dev, "SourceButton").Patterns.ExpandCollapse.Pattern.Collapse();
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("SourceRadioButtons")) == null, DefaultTimeout, "the source picker to close");
        Thread.Sleep(500); // let the close animation finish before the next open
    }

    private static object ReadSourcePicker(DevApp dev)
    {
        var group = OpenSourcePicker(dev);
        var radios = group.FindAllDescendants(cf => cf.ByControlType(ControlType.RadioButton));
        var options = radios.Select(r => new
        {
            Name = Text(r),
            Selected = r.Patterns.SelectionItem.Pattern.IsSelected.Value,
        }).ToArray();
        CloseSourcePicker(dev);
        return options;
    }

    private static void SelectSource(DevApp dev, string source)
    {
        var group = OpenSourcePicker(dev);
        var radio = group.FindFirstDescendant(cf => cf.ByControlType(ControlType.RadioButton).And(cf.ByName(source)))
            ?? throw new InvalidOperationException($"No '{source}' option in the source picker.");
        radio.Patterns.SelectionItem.Pattern.Select();
        var albumMode = Find(dev, "AlbumModeSwitch").Patterns.Toggle.Pattern;
        if (albumMode.ToggleState.Value == ToggleState.On) albumMode.Toggle();
        CloseSourcePicker(dev);
    }

    private static (object Summary, int Count) Search(DevApp dev, string source)
    {
        SubmitSearch(dev, Query);

        // The previous search's rows stay until the new search clears them, so wait for rows from this source.
        // Rows are virtualized, so only the visible ones are in the tree; their source badges are enough here.
        var expectedBadge = source.ToLowerInvariant();
        string[] VisibleBadges() => Find(dev, "CustomListView")
            .FindAllDescendants(cf => cf.ByAutomationId("SourceBadge")).Select(Text).Distinct().ToArray();
        try
        {
            WaitUntil(() => VisibleBadges() is [var only] && only == expectedBadge, ResultsTimeout, $"{source} results");
        }
        catch (TimeoutException) when (Text(Find(dev, "ResultsText")) == "No results")
        {
            // The service answered with nothing, which happens now and then under load. Try once more; a broken
            // search fails both times.
            SubmitSearch(dev, Query);
            WaitUntil(() => VisibleBadges() is [var only] && only == expectedBadge, ResultsTimeout, $"{source} results (second try)");
        }

        // YouTube loads details one result at a time. A quiet three seconds can occur between results while the
        // search is still running, so wait for the stop button to disappear before counting or adding tracks.
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("StopSearchButton")) is not { } stop
            || stop.Properties.IsOffscreen.ValueOrDefault, TimeSpan.FromSeconds(90), $"{source} search completion");
        var resultsText = Text(Find(dev, "ResultsText"));
        var match = System.Text.RegularExpressions.Regex.Match(resultsText, @"^Add (\d+) to queue$");
        var count = match.Success ? int.Parse(match.Groups[1].Value) : -1;

        // The first page holds one page of results (the source's initial chunk size). YouTube drops results whose
        // details it can't load when YouTube bot-checks the app (see "YouTube Music library flakiness"), so for
        // YouTube only the range is pinned.
        object resultCount = source == "YouTube"
            ? (count is >= 1 and <= 10 ? "between 1 and 10" : resultsText)
            : resultsText;

        return (new { Source = source, Results = resultCount, VisibleSourceBadges = VisibleBadges() }, count);
    }

    // The Queue page has no checkboxes of its own, so while a dialog is open every checkbox in the tree is the dialog's.
    // Reading them by name instead of AutomationId keeps this valid when Phase 2 generates them from the source list.
    private static object[] ReadDialogCheckBoxes(DevApp dev) => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox))
        .Select(c => (object)new { Name = Text(c), Checked = c.AsCheckBox().IsChecked, Enabled = c.IsEnabled })
        .ToArray();

    private static object ReadConversionDialog(DevApp dev)
    {
        Invoke(Find(dev, "ConvertDialogOpenButton"));
        Find(dev, "OutputComboBox");
        WaitUntil(() => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Length > 0, DefaultTimeout, "the conversion dialog's checkboxes");

        var inputs = ReadDialogCheckBoxes(dev);

        var combo = Find(dev, "OutputComboBox").AsComboBox();
        var outputs = combo.Items.Select(i => i.Text).ToArray();
        var selectedOutput = combo.SelectedItem?.Text;
        combo.Collapse();

        var closeText = CloseDialog(dev);
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("OutputComboBox")) == null, DefaultTimeout, "the conversion dialog to close");
        return new { Inputs = inputs, Outputs = outputs, SelectedOutput = selectedOutput, CloseButton = closeText };
    }

    private static object ReadClearDialog(DevApp dev)
    {
        Invoke(Find(dev, "ClearButton"));
        WaitUntil(() => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Length > 0, DefaultTimeout, "the clear dialog's checkboxes");

        var options = ReadDialogCheckBoxes(dev);

        var closeText = CloseDialog(dev);
        WaitUntil(() => FindAll(dev, cf => cf.ByControlType(ControlType.CheckBox)).Length == 0, DefaultTimeout, "the clear dialog to close");
        return new { Options = options, CloseButton = closeText };
    }
}
