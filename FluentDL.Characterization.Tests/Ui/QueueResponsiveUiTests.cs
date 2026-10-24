using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Definitions;
using FluentDL.Models;
using FluentDL.ViewModels;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using static FluentDL.Characterization.Tests.Ui.QueueWorkflowSteps;
using static FluentDL.Characterization.Tests.Ui.UiSteps;

namespace FluentDL.Characterization.Tests.Ui;

[TestClass]
[TestCategory("UI")]
[TestCategory("Workflow")]
[DoNotParallelize]
public class QueueResponsiveUiTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast { public uint Size; public uint Flags; public IntPtr Scheme; }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool ContrastParameters(uint action, uint size, ref HighContrast value, uint flags);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(3 * 60 * 1000)]
    public void NarrowQueueKeepsTitlesReadableAndStacksTheSelectedPreview(bool compact)
    {
        RequireSandbox();
        if (Environment.UserName != "WDAGUtilityAccount") Assert.Inconclusive("Window and Contrast changes are restricted to Windows Sandbox.");
        using var fixture = new Fixture(compact);
        using var dev = DevApp.Launch();
        Find(dev, "SearchBox", TimeSpan.FromSeconds(60));
        Navigate(dev, "Queue");
        Find(dev, "CustomListView");
        var titleId = compact ? "CompactQueueTitle" : "QueueCardTitle";

        Step(dev, "narrow-empty-preview-" + compact, () =>
        {
            Resize(dev, 800, 740);
            WaitUntil(() => Find(dev, "CustomListView").BoundingRectangle.Width >= 400, DefaultTimeout, "a readable narrow queue");
            var pane = TryFind(dev, cf => cf.ByAutomationId("PreviewContent"));
            Assert.IsTrue(pane == null || pane.IsOffscreen || pane.BoundingRectangle.Width == 0, "An empty preview must not reserve narrow queue width.");
            Assert.IsTrue(Find(dev, titleId).BoundingRectangle.Width >= 120, "The title must not collapse to a few pixels.");
            Capture(dev, "queue-narrow-empty-" + compact + ".png");
        });

        Step(dev, "narrow-selected-preview-" + compact, () =>
        {
            var row = Find(dev, "CustomListView").FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))[0];
            row.Patterns.SelectionItem.Pattern.Select();
            WaitUntil(() => Text(Find(dev, "PreviewTitleText")) == "Responsive album", DefaultTimeout, "the selected album preview");
            WaitUntil(() => Find(dev, "PreviewContent").BoundingRectangle.Top >= Find(dev, "CustomListView").BoundingRectangle.Bottom,
                DefaultTimeout, "the preview below, not beside, the narrow queue");
            Assert.IsTrue(Find(dev, titleId).BoundingRectangle.Width >= 120);
            Assert.IsTrue(Find(dev, "PreviewContent").BoundingRectangle.Right <= dev.MainWindow.BoundingRectangle.Right);
            WaitUntil(() => !Find(dev, "PreviewTitleText").IsOffscreen, DefaultTimeout,
                "the selected title to appear without scrolling past large artwork");
            Capture(dev, "queue-narrow-selected-" + compact + ".png");
        });

        Step(dev, "wide-selected-preview-" + compact, () =>
        {
            Resize(dev, 1400, 900);
            WaitUntil(() => Find(dev, "PreviewContent").BoundingRectangle.Left >= Find(dev, "CustomListView").BoundingRectangle.Right,
                DefaultTimeout, "the preview beside the wide queue");
            Assert.AreEqual("Responsive album", Text(Find(dev, "PreviewTitleText")));
            Assert.IsTrue(Find(dev, "CustomListView").BoundingRectangle.Width >= 400);
            Capture(dev, "queue-wide-selected-" + compact + ".png");
        });

        Step(dev, "narrow-round-trip-" + compact, () =>
        {
            Resize(dev, 800, 740);
            WaitUntil(() => Find(dev, "PreviewContent").BoundingRectangle.Top >= Find(dev, "CustomListView").BoundingRectangle.Bottom,
                DefaultTimeout, "the preview to stack after resizing back");
            Assert.AreEqual("Responsive album", Text(Find(dev, "PreviewTitleText")));
            Assert.AreEqual(2, SavedRows().Length, "Resizing and selection must not change the queue.");
            dev.AssertHealthy();
        });
        if (compact)
        {
            Step(dev, "narrow-selected-contrast", () =>
            {
                using var contrast = new ContrastScope();
                Thread.Sleep(500); // Let the guest app process the system theme notification before capture.
                Assert.IsTrue(Find(dev, titleId).BoundingRectangle.Width >= 120);
                Capture(dev, "queue-narrow-selected-contrast.png");
            });
        }
    }

    private static void Resize(DevApp dev, int width, int height)
    {
        using var process = Process.GetProcessById(dev.App.ProcessId);
        Assert.IsTrue(MoveWindow(process.MainWindowHandle, 100, 100, width, height, true), "The test window must resize.");
    }

    private sealed class ContrastScope : IDisposable
    {
        private HighContrast previous;
        private readonly string? scheme;

        internal ContrastScope()
        {
            previous.Size = (uint)Marshal.SizeOf<HighContrast>();
            if (!ContrastParameters(0x42, previous.Size, ref previous, 0)) throw new InvalidOperationException("Cannot read the guest Contrast setting.");
            scheme = Marshal.PtrToStringUni(previous.Scheme);
            Set(previous.Flags | 1, "High Contrast Black");
        }

        private static void Set(uint flags, string? scheme)
        {
            var buffer = Marshal.StringToHGlobalUni(scheme);
            try
            {
                var value = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>(), Flags = flags, Scheme = buffer };
                if (!ContrastParameters(0x43, value.Size, ref value, 2)) throw new InvalidOperationException("Cannot change the guest Contrast setting.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose() => Set(previous.Flags, scheme);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Windows.Storage.ApplicationData data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(DevApp.FindAumid().Split('!')[0]);
        private readonly Dictionary<string, object?> settings;
        private readonly List<(string Hash, string Json, byte[]? Image, string Created)> saved = [];
        private readonly string database = Path.Combine(DevApp.LocalStateDirectory, "fluentdl.db");

        internal Fixture(bool compact)
        {
            var keys = new[] { "auto_play", "notify_update", "queue_compact_layout", "AppBackgroundRequestedTheme" };
            settings = keys.ToDictionary(key => key, key => data.LocalSettings.Values.TryGetValue(key, out var value) ? value : null);
            data.LocalSettings.Values["auto_play"] = "false";
            data.LocalSettings.Values["notify_update"] = "false";
            data.LocalSettings.Values["queue_compact_layout"] = JsonConvert.SerializeObject(compact);
            data.LocalSettings.Values["AppBackgroundRequestedTheme"] = JsonConvert.SerializeObject(compact ? "Dark" : "Light");
            Directory.CreateDirectory(DevApp.LocalStateDirectory);
            using var db = Open();
            using (var create = db.CreateCommand())
            {
                create.CommandText = "CREATE TABLE IF NOT EXISTS Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL)";
                create.ExecuteNonQuery();
            }
            using (var read = db.CreateCommand())
            {
                read.CommandText = "SELECT Hash, JsonText, Image, CreatedUtc FROM Items";
                using var reader = read.ExecuteReader();
                while (reader.Read()) saved.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2), reader.GetString(3)));
            }
            var tracks = Enumerable.Range(1, 3).Select(i => new SongSearchObject { Id = "fixture-track-" + i, Source = "youtube", Title = "Fixture track " + i }).ToList();
            var albums = Enumerable.Range(0, 2).Select(i => new QueueObject(new AlbumSearchObject
            {
                Id = "responsive-album-" + i, Source = "youtube", Title = i == 0 ? "Responsive album" : "Second album",
                Artists = "Responsive fixture", Duration = "90", TracksCount = 3, TrackList = tracks,
            }) { QueueCounter = i });
            Replace(db, albums.Select(album => ("album:" + album.Source + album.Id, album.ToString(), (byte[]?)null, DateTime.UtcNow.ToString("o"))));
        }

        private SqliteConnection Open()
        {
            var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
            db.Open();
            return db;
        }

        private static void Replace(SqliteConnection db, IEnumerable<(string Hash, string Json, byte[]? Image, string Created)> rows)
        {
            using var transaction = db.BeginTransaction();
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Items";
            command.ExecuteNonQuery();
            foreach (var row in rows)
            {
                command.CommandText = "INSERT INTO Items VALUES (@h, @j, @i, @t)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@h", row.Hash);
                command.Parameters.AddWithValue("@j", row.Json);
                command.Parameters.AddWithValue("@i", row.Image ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@t", row.Created);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        public void Dispose()
        {
            using (var db = Open()) Replace(db, saved);
            foreach (var (key, value) in settings)
                if (value == null) data.LocalSettings.Values.Remove(key); else data.LocalSettings.Values[key] = value;
        }
    }
}
