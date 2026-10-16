using FluentDL.Characterization.Tests.Ui;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class UiRunLogTests
{
    [TestMethod]
    public void IgnoresOldFailuresButFindsNewFailuresAndNewLogFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "ui-log-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "old.log");
            File.WriteAllText(path, "[FTL] previous session\n");
            var session = new UiRunLog(folder);
            File.AppendAllText(path, "[INF] startup\n");
            Assert.IsFalse(session.HasNewFatal());
            File.AppendAllText(path, "[FTL] current session\n");
            Assert.IsTrue(session.HasNewFatal());
            var next = new UiRunLog(folder);
            File.WriteAllText(Path.Combine(folder, "next-day.log"), "[FTL] after midnight\n");
            Assert.IsTrue(next.HasNewFatal());
        }
        finally { Directory.Delete(folder, true); }
    }
}
