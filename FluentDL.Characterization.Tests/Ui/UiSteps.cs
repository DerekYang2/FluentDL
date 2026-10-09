using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>Small waiting and lookup helpers on top of FlaUI, for the dev app's main window and its popups.</summary>
internal static class UiSteps
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition()) return;
            }
            catch
            {
                // The tree changes while the app works; try again.
            }
            Thread.Sleep(250);
        }
        throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} s waiting for {what}.");
    }

    /// <summary>
    /// Finds an element by AutomationId in the main window, or in a popup window of the same process
    /// (flyouts and dialogs can be hosted outside the main window's tree).
    /// </summary>
    public static AutomationElement Find(DevApp dev, string automationId, TimeSpan? timeout = null) =>
        Find(dev, cf => cf.ByAutomationId(automationId), automationId, timeout);

    public static AutomationElement Find(DevApp dev, Func<ConditionFactory, ConditionBase> condition, string what, TimeSpan? timeout = null)
    {
        AutomationElement? found = null;
        WaitUntil(() => (found = TryFind(dev, condition)) != null, timeout ?? DefaultTimeout, what);
        return found!;
    }

    public static AutomationElement? TryFind(DevApp dev, Func<ConditionFactory, ConditionBase> condition)
    {
        var inWindow = dev.MainWindow.FindFirstDescendant(condition);
        if (inWindow != null) return inWindow;

        var pid = dev.App.ProcessId;
        foreach (var top in dev.Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(pid)))
        {
            var match = top.FindFirstDescendant(condition);
            if (match != null) return match;
        }
        return null;
    }

    /// <summary>All matching elements in the main window and in other top-level windows of the same process, in tree order.</summary>
    public static AutomationElement[] FindAll(DevApp dev, Func<ConditionFactory, ConditionBase> condition)
    {
        var results = new List<AutomationElement>(dev.MainWindow.FindAllDescendants(condition));
        var pid = dev.App.ProcessId;
        foreach (var top in dev.Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(pid)))
        {
            if (top.Equals(dev.MainWindow)) continue;
            results.AddRange(top.FindAllDescendants(condition));
        }
        return results.ToArray();
    }

    public static string Text(AutomationElement element) => element.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>Waits until a value stops changing for <paramref name="quiet"/>, e.g. a result count while results stream in.</summary>
    public static string WaitForStable(Func<string> read, TimeSpan quiet, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = read();
        var since = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            string current;
            try { current = read(); } catch { continue; }
            if (current != last)
            {
                last = current;
                since = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - since >= quiet)
            {
                return current;
            }
        }
        throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} s waiting for {what} to settle (last: '{last}').");
    }

    public static void Navigate(DevApp dev, string pageName)
    {
        var item = Find(dev, cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(pageName)), $"navigation item '{pageName}'");
        item.Patterns.SelectionItem.Pattern.Select();
    }

    public static void Invoke(AutomationElement element) => element.Patterns.Invoke.Pattern.Invoke();

    /// <summary>
    /// Saves a screenshot of the app window and its UI Automation tree to %TEMP%\opencode, to see what the app showed
    /// when a step failed.
    /// </summary>
    public static string SaveFailureEvidence(DevApp dev, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-ui-failures");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("HHmmss");
        try
        {
            FlaUI.Core.Capturing.Capture.Element(dev.MainWindow).ToFile(Path.Combine(dir, $"{stamp}-{name}.png"));
        }
        catch
        {
        }
        try
        {
            var builder = new System.Text.StringBuilder();
            UiProbe.DumpTree(dev.MainWindow, builder, 0, 30);
            File.WriteAllText(Path.Combine(dir, $"{stamp}-{name}.txt"), builder.ToString());
        }
        catch
        {
        }
        return dir;
    }

    /// <summary>Clicks a ContentDialog's close button ("Close" or "Cancel"); its AutomationId is CloseButton.</summary>
    public static string CloseDialog(DevApp dev)
    {
        var button = Find(dev, "CloseButton");
        var text = Text(button);
        Invoke(button);
        return text;
    }
}
