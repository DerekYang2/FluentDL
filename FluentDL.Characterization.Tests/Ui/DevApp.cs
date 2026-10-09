using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Windows.Management.Deployment;

namespace FluentDL.Characterization.Tests.Ui;

/// <summary>
/// Starts and stops the "FluentDL (Dev)" package that tools/ui-tests/Install-DevPackage.ps1 installs.
/// UI tests open a real window on the desktop, so they only run when FLUENTDL_UI_TESTS=1.
/// </summary>
internal sealed class DevApp : IDisposable
{
    public const string PackageName = "DerekYang2.FluentDL.Dev";

    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; private set; }

    private DevApp(Application app, UIA3Automation automation, Window mainWindow)
    {
        App = app;
        Automation = automation;
        MainWindow = mainWindow;
    }

    public static void RequireUiTests()
    {
        if (Environment.GetEnvironmentVariable("FLUENTDL_UI_TESTS") != "1")
        {
            Assert.Inconclusive("UI tests take over the mouse and keyboard. Set FLUENTDL_UI_TESTS=1 to run them.");
        }
    }

    public static string FindAumid()
    {
        var package = FindPackage();
        return $"{package.Id.FamilyName}!App";
    }

    private static Windows.ApplicationModel.Package FindPackage()
    {
        var package = new PackageManager().FindPackagesForUser(string.Empty)
            .FirstOrDefault(p => p.Id.Name == PackageName);
        if (package == null)
        {
            Assert.Inconclusive($"{PackageName} is not installed. Run tools/ui-tests/Install-DevPackage.ps1 first.");
        }
        return package!;
    }

    /// <summary>
    /// Writes settings into the dev app's own settings store while it is closed, in the same format the app's
    /// LocalSettingsService writes (each value JSON-serialized with Newtonsoft). Used to sign the dev app in to
    /// Qobuz and Deezer, which search needs.
    /// </summary>
    public static void SeedSettings(IReadOnlyDictionary<string, string> settings)
    {
        var data = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(FindPackage().Id.FamilyName);
        foreach (var (key, value) in settings)
        {
            data.LocalSettings.Values[key] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
    }

    /// <summary>Launches the dev app and waits for its main window. Startup can wait up to 30 s for the music services.</summary>
    public static DevApp Launch(TimeSpan? timeout = null)
    {
        RequireUiTests();
        var aumid = FindAumid();
        var app = Application.LaunchStoreApp(aumid);
        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, timeout ?? TimeSpan.FromSeconds(60))
            ?? throw new InvalidOperationException("The dev app's main window did not appear.");
        return new DevApp(app, automation, window);
    }

    public void Dispose()
    {
        try
        {
            App.Close();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!App.HasExited && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
            }
            if (!App.HasExited)
            {
                App.Kill();
            }
        }
        catch
        {
            try { App.Kill(); } catch { }
        }
        App.Dispose();
        Automation.Dispose();
    }
}
