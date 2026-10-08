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
/// Uses the real keyboard for Enter, so it only runs with FLUENTDL_UI_TESTS=1. Install the dev package first with
/// tools/ui-tests/Install-DevPackage.ps1; it starts with empty settings, which this flow relies on.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SourceUiTests
{
    private static readonly string[] Sources = ["Deezer", "Qobuz", "Spotify", "YouTube"];
    private const string Query = "daft punk get lucky";

    [TestMethod]
    [Timeout(15 * 60 * 1000)]
    public void SourcePickerSearchAndQueueDialogs()
    {
        DevApp.RequireUiTests();
        var state = new Dictionary<string, object?>();

        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));

            state["NavigationItems"] = dev.MainWindow
                .FindAllDescendants(cf => cf.ByClassName("Microsoft.UI.Xaml.Controls.NavigationViewItem"))
                .Select(Text).ToArray();

            state["SourcePickerOnFirstStart"] = ReadSourcePicker(dev);

            var searches = new List<object>();
            foreach (var source in Sources)
            {
                SelectSource(dev, source);
                searches.Add(Search(dev, source));

                if (source is "Deezer" or "YouTube")
                {
                    Invoke(Find(dev, "AddToQueueButton"));
                    WaitUntil(() => Find(dev, "AddToQueueButton").IsEnabled, TimeSpan.FromMinutes(2), "adding results to the queue");
                }
            }
            state["Searches"] = searches;

            Navigate(dev, "Queue");
            state["QueueCount"] = WaitForStable(() => Text(Find(dev, "CountText")), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), "queue count");
            state["ConversionDialog"] = ReadConversionDialog(dev);
            state["ClearDialog"] = ReadClearDialog(dev);
        }

        // The source picked last (YouTube) is saved when the picker closes and restored on the next start.
        using (var dev = DevApp.Launch())
        {
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            state["SourcePickerAfterRestart"] = ReadSourcePicker(dev);
        }

        Snapshot.Match("Ui.SourcePickerSearchAndQueueDialogs", state);
    }

    private static object ReadSourcePicker(DevApp dev)
    {
        var button = Find(dev, "SourceButton");
        button.Patterns.ExpandCollapse.Pattern.Expand();
        var group = Find(dev, "SourceRadioButtons");
        var radios = group.FindAllDescendants(cf => cf.ByControlType(ControlType.RadioButton));
        var options = radios.Select(r => new
        {
            Name = Text(r),
            Selected = r.Patterns.SelectionItem.Pattern.IsSelected.Value,
        }).ToArray();
        button.Patterns.ExpandCollapse.Pattern.Collapse();
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("SourceRadioButtons")) == null, DefaultTimeout, "the source picker to close");
        return options;
    }

    private static void SelectSource(DevApp dev, string source)
    {
        var button = Find(dev, "SourceButton");
        button.Patterns.ExpandCollapse.Pattern.Expand();
        var group = Find(dev, "SourceRadioButtons");
        var radio = group.FindFirstDescendant(cf => cf.ByControlType(ControlType.RadioButton).And(cf.ByName(source)))
            ?? throw new InvalidOperationException($"No '{source}' option in the source picker.");
        radio.Patterns.SelectionItem.Pattern.Select();
        button.Patterns.ExpandCollapse.Pattern.Collapse();
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("SourceRadioButtons")) == null, DefaultTimeout, "the source picker to close");
    }

    private static object Search(DevApp dev, string source)
    {
        TypeAndSubmit(dev, Find(dev, cf => cf.ByAutomationId("TextBox").And(cf.ByClassName("TextBox")), "search text box"), Query);

        // The previous search's rows stay until the new search clears them, so wait for rows from this source.
        // Rows are virtualized, so only the visible ones are in the tree; their source badges are enough here.
        var expectedBadge = source.ToLowerInvariant();
        string[] VisibleBadges() => Find(dev, "CustomListView")
            .FindAllDescendants(cf => cf.ByAutomationId("SourceBadge")).Select(Text).Distinct().ToArray();
        WaitUntil(() => VisibleBadges() is [var only] && only == expectedBadge, TimeSpan.FromMinutes(2), $"{source} results");

        var resultsText = WaitForStable(() => Text(Find(dev, "ResultsText")), TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(3), $"{source} result count");
        return new { Source = source, ResultsText = resultsText, VisibleSourceBadges = VisibleBadges() };
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
