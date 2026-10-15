using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// The Settings page's sign-in: the login state after startup, then each source's Update button, with the message it
/// shows and the login state after it. Compared with a baseline recorded from the pre-refactor build.
/// Only runs with FLUENTDL_UI_TESTS=1. See SourceUiTests for setup.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SettingsUiTests
{
    private static readonly string[] Sources = ["Deezer", "Qobuz", "Spotify"];

    [TestMethod]
    [Timeout(4 * 60 * 1000)]
    public void SignInFromSettings()
    {
        DevApp.RequireUiTests();
        DevApp.SeedTestAccounts();
        var state = new Dictionary<string, object?>();

        using (var dev = DevApp.Launch())
        {
            try
            {
                Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
                Navigate(dev, "Settings");
                // The page sets the login indicators after loading its settings, so wait for them to settle.
                WaitForStable(() => string.Join("|", Sources.Select(s => LoginState(dev, s))), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20), "the login indicators");
                state["AfterStartup"] = Sources.ToDictionary(s => s, s => LoginState(dev, s));

                var updates = new Dictionary<string, object>();
                foreach (var source in Sources)
                {
                    Expand(dev, $"{source}Expander");
                    var before = InfoBarText(dev);
                    Invoke(FindScrolling(dev, $"{source}UpdateButton"));
                    updates[source] = new { Message = WaitForNewMessage(dev, before), LoginState = LoginState(dev, source) };
                }
                state["AfterUpdate"] = updates;

                // A wrong ARL: the error message, and Deezer shows as signed out. The next run seeds the real one again.
                Expand(dev, "DeezerExpander");
                var arlInput = FindScrolling(dev, "DeezerARLInput");
                var arlField = (arlInput.Patterns.Value.IsSupported ? arlInput : null)
                    ?? arlInput.FindAllDescendants().FirstOrDefault(e => e.Patterns.Value.IsSupported)
                    ?? throw new InvalidOperationException("The ARL box has no editable field.");
                arlField.Patterns.Value.Pattern.SetValue("not-a-real-arl");
                var beforeBad = InfoBarText(dev);
                Invoke(FindScrolling(dev, "DeezerUpdateButton"));
                state["WrongArl"] = new { Message = WaitForNewMessage(dev, beforeBad), LoginState = LoginState(dev, "Deezer") };
            }
            catch (Exception e)
            {
                var dir = SaveFailureEvidence(dev, "settings");
                throw new AssertFailedException($"{e.Message} Screenshot and UI tree saved to {dir}.", e);
            }
        }

        Snapshot.Match("Ui.SignInFromSettings", state);
    }

    private static string LoginState(DevApp dev, string source) => Text(Find(dev, $"{source}InfoButtonText"));

    // The first new message in the page's info bar, or null if none shows within 30 s (a source can report nothing).
    // Messages close after a few seconds, and a closed bar reads as empty, so the first non-empty new text counts.
    private static string? WaitForNewMessage(DevApp dev, string before)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var text = InfoBarText(dev);
            if (text != before && text != "")
            {
                return text;
            }
            Thread.Sleep(100);
        }
        return null;
    }

    private static string InfoBarText(DevApp dev) =>
        TryFind(dev, cf => cf.ByAutomationId("InfoBarTextBlock")) is { } text ? Text(text) : "";

    // Settings cards below the visible area aren't created until they scroll into view, so scroll the page down
    // through UI Automation until the element exists.
    private static AutomationElement FindScrolling(DevApp dev, string automationId)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (TryFind(dev, cf => cf.ByAutomationId(automationId)) is { } found)
            {
                return found;
            }
            var scroller = dev.MainWindow.FindAllDescendants()
                .Where(e => e.Patterns.Scroll.IsSupported && e.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                .OrderByDescending(e => e.BoundingRectangle.Height)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No vertical scroller on the page.");
            scroller.Patterns.Scroll.Pattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            Thread.Sleep(400);
        }
        throw new TimeoutException($"{automationId} did not appear while scrolling the page.");
    }

    // The toolkit's SettingsExpander wraps a WinUI Expander, which is the part that expands. Expand is sometimes
    // ignored (for example during another expander's animation), so retry until the state says expanded.
    private static void Expand(DevApp dev, string automationId)
    {
        var expander = Find(dev, automationId).FindFirstDescendant(cf => cf.ByClassName("Microsoft.UI.Xaml.Controls.Expander"))
            ?? throw new InvalidOperationException($"{automationId} has no Expander inside.");
        var pattern = expander.Patterns.ExpandCollapse.Pattern;
        for (var attempt = 1; attempt <= 4 && pattern.ExpandCollapseState.Value != ExpandCollapseState.Expanded; attempt++)
        {
            pattern.Expand();
            Thread.Sleep(400 * attempt);
        }
        if (pattern.ExpandCollapseState.Value != ExpandCollapseState.Expanded)
        {
            throw new InvalidOperationException($"{automationId} did not expand.");
        }
    }
}
