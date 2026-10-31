using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentDL.Characterization.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

internal static class QueueWorkflowSteps
{
    internal sealed record SavedRow(string Source, string Id, string Title, bool Album, int Tracks);

    public static void RequireSandbox()
    {
        DevApp.RequireUiTests();
        if (Environment.GetEnvironmentVariable("FLUENTDL_UI_REAL_INPUT") != "1")
            Assert.Inconclusive("Queue workflows change the test queue. Run them with Run-UiTests.ps1 in the Sandbox.");
    }

    public static void Step(DevApp dev, string name, Action action)
    {
        try
        {
            dev.AssertHealthy();
            action();
            dev.AssertHealthy();
        }
        catch (Exception e)
        {
            var directory = SaveFailureEvidence(dev, "workflow-" + name);
            throw new AssertFailedException($"Workflow step '{name}' failed: {e.Message} Evidence: {directory}", e);
        }
    }

    public static void SetAlbumMode(DevApp dev, bool enabled)
    {
        SourceUiTests.OpenSourcePicker(dev);
        var toggle = Find(dev, "AlbumModeSwitch").Patterns.Toggle.Pattern;
        if ((toggle.ToggleState.Value == ToggleState.On) != enabled) toggle.Toggle();
        SourceUiTests.CloseSourcePicker(dev);
    }

    public static void AddLink(DevApp dev, string source, string url, bool album)
    {
        LoadLink(dev, source, url, album);
        Invoke(Find(dev, "AddToQueueButton"));
        WaitUntil(() => Find(dev, "AddToQueueButton").IsEnabled, DefaultTimeout, "adding the result to the queue");
        SetAlbumMode(dev, false);
    }

    public static void LoadLink(DevApp dev, string source, string url, bool album)
    {
        Navigate(dev, "Search");
        SetAlbumMode(dev, album);
        SubmitSearch(dev, url);
        WaitUntil(() => Text(Find(dev, "ResultsText")) == "Add 1 to queue"
            && Find(dev, "CustomListView").FindAllDescendants(cf => cf.ByAutomationId("SourceBadge")) is [var badge]
            && Text(badge) == source, TimeSpan.FromSeconds(40), $"the {source} link result");
    }

    public static AutomationElement[] Rows(DevApp dev) => Find(dev, "CustomListView")
        .FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));

    // Read the actual guest database independently of QueueObject's serialization, which is what these tests check.
    public static SavedRow[] SavedRows()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DevApp.LocalStateDirectory, "fluentdl.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT JsonText FROM Items";
        using var reader = command.ExecuteReader();
        var rows = new List<(int Position, SavedRow Row)>();
        while (reader.Read())
        {
            var json = JObject.Parse(reader.GetString(0));
            var count = json["AlbumTracksCount"];
            rows.Add((json.Value<int?>("QueueCounter") ?? 0,
                new SavedRow(json.Value<string>("Source")!, json.Value<string>("Id")!, json.Value<string>("Title")!,
                    count?.Type == JTokenType.Integer, count?.Type == JTokenType.Integer ? count.Value<int>() : 0)));
        }
        return rows.OrderBy(r => r.Position).Select(r => r.Row).ToArray();
    }

    public static SavedRow[] WaitSaved(int count)
    {
        SavedRow[] saved = [];
        WaitUntil(() => (saved = SavedRows()).Length == count, DefaultTimeout, "the queue rows to reach the database");
        return saved;
    }

    public static void PreviewEveryRow(DevApp dev, SavedRow[] expected, string stage)
    {
        Navigate(dev, "Queue");
        WaitUntil(() => Rows(dev).Length == expected.Length, DefaultTimeout, "the expected queue rows");
        for (var i = 0; i < expected.Length; i++)
        {
            var item = expected[i];
            Step(dev, $"{stage}-preview-{i}-{item.Source}", () =>
            {
                // Clear the old preview so a stale title or track list cannot satisfy the next assertion.
                foreach (var selected in Rows(dev).Where(r => r.Patterns.SelectionItem.Pattern.IsSelected.Value))
                    selected.Patterns.SelectionItem.Pattern.RemoveFromSelection();
                var row = Rows(dev)[i];
                if (row.Patterns.ScrollItem.IsSupported) row.Patterns.ScrollItem.Pattern.ScrollIntoView();
                row.Patterns.SelectionItem.Pattern.Select();
                WaitUntil(() => Text(Find(dev, "PreviewTitleText")) == item.Title, TimeSpan.FromSeconds(25), "the selected row's preview title");
                if (item.Album)
                {
                    WaitUntil(() => Text(Find(dev, "TrackListHeader")) == $"{item.Tracks} Tracks"
                        && Find(dev, "AlbumTrackListView").FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0,
                        TimeSpan.FromSeconds(25), "the album preview's actual track list");
                }
                else
                {
                    // Layout-only panels have no UIA peer; the detail labels are siblings of the title.
                    WaitUntil(() => Find(dev, "PreviewTitleText").Parent.FindAllChildren(cf => cf.ByControlType(ControlType.Text))
                        .Any(t => Text(t) is "Artists" or "Contributing Artists"), TimeSpan.FromSeconds(25), "track details after the source responds");
                }
                WaitUntil(() => HasFixtureArtwork(dev), TimeSpan.FromSeconds(15), "the fixture cover to render");
                dev.AssertHealthy();
                Capture(dev, $"{stage}-{i}-{item.Source}.png");
            });
        }
    }

    public static void SetCompact(DevApp dev, bool enabled)
    {
        var toggle = Find(dev, "CompactQueueButton").Patterns.Toggle.Pattern;
        if ((toggle.ToggleState.Value == ToggleState.On) != enabled) toggle.Toggle();
        WaitUntil(() => (toggle.ToggleState.Value == ToggleState.On) == enabled, DefaultTimeout, "the queue layout");
    }

    public static void Convert(DevApp dev, string[] inputs, string output)
    {
        QueueConversionUiTests.Convert(dev, inputs, output);
        var matches = 0;
        foreach (var tab in new[] { "SuccessTab", "WarningTab" })
        {
            Find(dev, tab).Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(250);
            matches += Find(dev, "ConversionListView").FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length;
        }
        Assert.IsTrue(matches > 0,
            "The workflow must convert an item, not just reach an empty summary.");
        CloseDialog(dev);
        WaitUntil(() => TryFind(dev, cf => cf.ByAutomationId("ConversionTabView")) == null, DefaultTimeout, "the summary to close");
    }

    public static void RestoreNewestBackup(DevApp dev)
    {
        Invoke(Find(dev, "RestoreQueueButton"));
        var combo = Find(dev, "RestoreQueueBackupComboBox").AsComboBox();
        combo.Expand();
        WaitUntil(() => combo.Items.Length > 0, DefaultTimeout, "the automatic backup");
        combo.Items[0].Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();
        Invoke(Find(dev, "PrimaryButton"));
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    public static void ChooseFile(DevApp dev, string path)
    {
        AutomationElement? picker = null;
        var owner = new IntPtr(dev.MainWindow.Properties.NativeWindowHandle.Value);
        WaitUntil(() => (picker = dev.MainWindow.FindFirstDescendant(cf => cf.ByClassName("#32770"))
            ?? dev.Automation.GetDesktop().FindAllChildren(cf => cf.ByClassName("#32770"))
                .FirstOrDefault(w => GetWindow(new IntPtr(w.Properties.NativeWindowHandle.Value), 4) == owner)) != null,
            DefaultTimeout, "the app's file picker");
        var name = picker!.FindFirstDescendant(cf => cf.ByAutomationId("1148"))
            ?? picker.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("File name:")))
            ?? throw new AssertFailedException("The app's file picker has no filename field.");
        var field = name.Patterns.Value.IsSupported ? name : name.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
        Assert.IsNotNull(field);
        // The modern picker can expose a Value setter without committing it to the Save operation. Real typing and
        // leaving the field commits its filename. These workflows are restricted to the guest desktop.
        field.Focus();
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type(path);
        FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
        WaitUntil(() => field.Patterns.Value.Pattern.Value.Value == path, DefaultTimeout, "the picker filename to be committed");
        var accept = picker.FindFirstDescendant(cf => cf.ByAutomationId("1"))
            ?? throw new AssertFailedException("The app's file picker has no Open/Save button.");
        Invoke(accept);
    }

    // Our fixtures all have multicolored covers. Sample the image's interior to distinguish loaded artwork from an
    // empty placeholder. This is not a general-purpose assertion for arbitrary, possibly monochrome artwork.
    private static bool HasFixtureArtwork(DevApp dev)
    {
        var image = Find(dev, "PreviewImage").BoundingRectangle;
        var window = dev.MainWindow.BoundingRectangle;
        if (image.Width < 16 || image.Height < 16) return false;
        using var bitmap = WindowBitmap(dev);
        var colors = new HashSet<int>();
        for (var y = 2; y < 9; y++)
            for (var x = 2; x < 9; x++)
            {
                var px = image.Left - window.Left + image.Width * x / 10;
                var py = image.Top - window.Top + image.Height * y / 10;
                if (px < 0 || py < 0 || px >= bitmap.Width || py >= bitmap.Height) return false;
                var c = bitmap.GetPixel(px, py);
                colors.Add((c.R / 16 << 8) | (c.G / 16 << 4) | c.B / 16);
            }
        return colors.Count >= 6;
    }

    public static void Capture(DevApp dev, string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "workflow-ui");
        Directory.CreateDirectory(directory);
        using var bitmap = WindowBitmap(dev);
        bitmap.Save(Path.Combine(directory, name));
    }

    private static System.Drawing.Bitmap WindowBitmap(DevApp dev)
    {
        var bounds = dev.MainWindow.BoundingRectangle;
        var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try { Assert.IsTrue(PrintWindow(new IntPtr(dev.MainWindow.Properties.NativeWindowHandle.Value), dc, 2)); }
        finally { graphics.ReleaseHdc(dc); }
        return bitmap;
    }
}
