using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// Writes the dev app's UI Automation tree to a text file, to find control names while writing UI tests.
/// Run with: $env:FLUENTDL_UI_TESTS='1'; dotnet test ... --filter "TestCategory=UIProbe"
/// </summary>
[TestClass]
[TestCategory("UIProbe")]
public class UiProbe
{
    [TestMethod]
    public void DumpTree()
    {
        using var dev = DevApp.Launch();
        // Give the splash screen time to finish and the shell to load.
        Thread.Sleep(TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("FLUENTDL_PROBE_WAIT") is { } s ? int.Parse(s) : 40));
        var window = dev.App.GetMainWindow(dev.Automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("The dev app's main window disappeared.");

        var builder = new StringBuilder();
        builder.AppendLine($"Window: '{window.Title}'");
        Dump(window, builder, 0);

        var path = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-ui-tree.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, builder.ToString());
        Console.WriteLine($"Wrote {path}");
    }

    private static void Dump(AutomationElement element, StringBuilder builder, int depth)
    {
        if (depth > 25) return;
        string id = "", name = "", cls = "", type = "";
        try { id = element.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
        try { name = element.Properties.Name.ValueOrDefault ?? ""; } catch { }
        try { cls = element.Properties.ClassName.ValueOrDefault ?? ""; } catch { }
        try { type = element.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
        if (name.Length > 60) name = name[..60] + "...";
        builder.Append(' ', depth * 2).AppendLine($"{type} id='{id}' name='{name}' class='{cls}'");

        AutomationElement[] children;
        try { children = element.FindAllChildren(); } catch { return; }
        foreach (var child in children) Dump(child, builder, depth + 1);
    }
}
