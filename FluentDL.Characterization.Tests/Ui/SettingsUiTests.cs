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
    private static readonly string[] Sources = ["Deezer", "Qobuz", "Spotify", "Tidal"];

    [TestMethod]
    public void TidalCombinedStatusShowsTheSavedDownloadAccount()
    {
        DevApp.RequireUiTests();
        var data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(DevApp.FindAumid().Split('!')[0]);
        var keys = new[] { "tidal_download_session", "tidal_refresh_token" };
        var previous = keys.ToDictionary(k => k, k => data.LocalSettings.Values.TryGetValue(k, out var value) ? value : null);
        var session = new Services.Tidal.TidalDownloadClient.Session("test-client", "test-access", "test-refresh",
            DateTimeOffset.UtcNow.AddHours(1), "CA", "downloads@example.test");
        var bytes = System.Security.Cryptography.ProtectedData.Protect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(session),
            null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        data.LocalSettings.Values["tidal_download_session"] = Newtonsoft.Json.JsonConvert.SerializeObject(Convert.ToBase64String(bytes));
        data.LocalSettings.Values.Remove("tidal_refresh_token");
        try
        {
            using var dev = DevApp.Launch();
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Settings");
            Assert.IsNull(TryFind(dev, cf => cf.ByAutomationId("TidalDownloadsExpander")));
            Expand(dev, "TidalExpander");
            var account = FindScrolling(dev, "TidalDownloadStatus");
            WaitUntil(() => Text(account).Contains("downloads@example.test"), TimeSpan.FromSeconds(10), "download identity");
            StringAssert.Contains(Text(account), "Subscription: Unavailable");
            var infoButton = Find(dev, "TidalInfoButton");
            var scroller = dev.MainWindow.FindAllDescendants()
                .Where(e => e.Patterns.Scroll.IsSupported && e.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
                .OrderByDescending(e => e.BoundingRectangle.Height).First();
            for (var attempt = 0; attempt < 12 && infoButton.Properties.IsOffscreen.ValueOrDefault; attempt++)
            {
                scroller.Patterns.Scroll.Pattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                Thread.Sleep(250);
            }
            Assert.IsFalse(infoButton.Properties.IsOffscreen.ValueOrDefault);
            Invoke(infoButton);
            var status = Text(Find(dev, "TidalInfoText"));
            StringAssert.Contains(status, "API:");
            StringAssert.Contains(status, "Only searching.");
            StringAssert.Contains(status, "Downloading:\ndownloads@example.test");
            Assert.IsFalse(status.Contains("Direct downloads"), "Downloads have no on/off setting any more.");
            var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-combined-ui");
            Directory.CreateDirectory(folder);
            SaveAppWindow(dev, Path.Combine(folder, "status.png"));
            FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Thread.Sleep(500);
            Assert.IsNotNull(FindScrolling(dev, "TidalUpdateButton"), "Save should be inside the TIDAL expander.");
            for (var page = 0; page < 3; page++)
            {
                SaveAppWindow(dev, Path.Combine(folder, $"expander-{page}.png"));
                scroller.Patterns.Scroll.Pattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                Thread.Sleep(400);
            }
        }
        finally
        {
            foreach (var (key, value) in previous)
                if (value == null) data.LocalSettings.Values.Remove(key); else data.LocalSettings.Values[key] = value;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    // Capture the app, not the user's foreground window. UI Automation still works when the app is covered.
    private static void SaveAppWindow(DevApp dev, string path)
    {
        var bounds = dev.MainWindow.BoundingRectangle;
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var context = graphics.GetHdc();
        try
        {
            var handle = new IntPtr(dev.MainWindow.Properties.NativeWindowHandle.Value);
            Assert.IsTrue(PrintWindow(handle, context, 2), "App window capture failed.");
        }
        finally { graphics.ReleaseHdc(context); }
        bitmap.Save(path);
    }

    // Run only against a diagnostic dev build whose login page is about:blank. This checks native WebView2 startup
    // without automated navigation to TIDAL, which has previously triggered its bot protection.
    [TestMethod]
    [TestCategory("UIProbe")]
    public void TidalEmbeddedBrowserStartsOffline()
    {
        DevApp.RequireUiTests();
        if (Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_BLANK_BROWSER_TEST") != "1")
            Assert.Inconclusive("Requires the about:blank diagnostic dev build.");
        var data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(DevApp.FindAumid().Split('!')[0]);
        var keys = new[] { "tidal_download_session" };
        var previous = keys.ToDictionary(k => k, k => data.LocalSettings.Values.TryGetValue(k, out var value) ? value : null);
        data.LocalSettings.Values.Remove("tidal_download_session");
        try
        {
            using var dev = DevApp.Launch();
            Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
            Navigate(dev, "Settings");
            Expand(dev, "TidalExpander");
            Invoke(FindScrolling(dev, "TidalDownloadLoginButton"));
            var status = Find(dev, "BrowserStatus");
            WaitUntil(() => Text(status) != "Starting embedded browser...", TimeSpan.FromSeconds(30), "WebView2 initialization result");
            var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-embedded-ui");
            Directory.CreateDirectory(folder);
            FlaUI.Core.Capturing.Capture.Element(dev.MainWindow).ToFile(Path.Combine(folder, "dialog.png"));
            Console.WriteLine("Embedded dialog status: " + Text(status));
            Assert.AreEqual("Embedded browser ready.", Text(status));
            var close = FindInRawView(dev, dev.MainWindow, "CloseButton", maxDepth: 25)
                ?? throw new InvalidOperationException("The download dialog has no close button in the raw UI tree.");
            Invoke(close);
            WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("BrowserStatus")) == null, TimeSpan.FromSeconds(10), "dialog closed");
        }
        finally
        {
            foreach (var (key, value) in previous)
                if (value == null) data.LocalSettings.Values.Remove(key); else data.LocalSettings.Values[key] = value;
        }
    }

    // Downloads have no on/off switch: signing in is always available, and the switch is gone.
    [TestMethod]
    public void TidalDownloadSignInIsAlwaysAvailable()
    {
        DevApp.RequireUiTests();
        using var dev = DevApp.Launch();
        Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
        Navigate(dev, "Settings");
        Expand(dev, "TidalExpander");
        var login = FindScrolling(dev, "TidalDownloadLoginButton");
        Assert.IsTrue(login.IsEnabled);
        StringAssert.Contains(Text(login), "downloads"); // "Sign in for downloads" or, with a saved session, "Sign out of downloads"
        Assert.IsTrue(FindScrolling(dev, "TidalDownloadQualityInput").IsEnabled);
        Assert.IsNull(TryFind(dev, cf => cf.ByAutomationId("TidalDirectToggle")));
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-direct-ui");
        Directory.CreateDirectory(folder);
        SaveAppWindow(dev, Path.Combine(folder, "settings.png"));
    }

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
