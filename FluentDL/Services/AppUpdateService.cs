using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using FluentDL.Contracts.Services;
using FluentDL.Core.Updates;
using FluentDL.Helpers;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.Storage;

namespace FluentDL.Services;

internal sealed class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly UpdateInstallation? installation;
    private readonly bool store;
    private readonly bool supported;
    private readonly string family = "";
    private readonly string packageRoot = "";
    private readonly string description;
    private StoreContext? context;
    private readonly Dictionary<AppUpdateOffer, PreparedUpdate> prepared = [];

    public AppUpdateService()
    {
        description = "Development installation. Use your build tools to update this copy.";
        if (!RuntimeHelper.IsMSIX) return;
        var package = Package.Current;
        var version = package.Id.Version;
        installation = new(package.Id.Name, package.Id.Publisher, new(version.Major, version.Minor, version.Build, version.Revision),
            package.Id.Architecture.ToString().ToLowerInvariant());
        family = package.Id.FamilyName;
        packageRoot = package.InstalledLocation.Path;
        if (package.IsDevelopmentMode) return;
        var channel = UpdateChannels.Select(installation, package.IsDevelopmentMode, package.SignatureKind == PackageSignatureKind.Store,
            package.SignatureKind == PackageSignatureKind.None, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        store = channel == UpdateChannel.MicrosoftStore;
        supported = channel != UpdateChannel.Unsupported;
        description = $"Installed version {installation.Version}. " + (store ? "Updates from Microsoft Store."
            : supported ? "Updates from GitHub. Installation needs administrator permission."
            : "This installation cannot update itself. Use its original installation method.");
    }

    public string InstallationDescription => description;
    public Uri HelpUri => new(store ? "ms-windows-store://pdp/?ProductId=9MX44KM97X7X" : GithubUpdates.Repository + "/releases");
    public string HelpLabel => store ? "Open Microsoft Store" : "Open releases";

    private StoreContext Store()
    {
        if (!App.MainWindow.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Store updates must run on the UI thread.");
        if (context != null) return context;
        context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        return context;
    }

    public async Task<AppUpdateOffer?> CheckAsync(CancellationToken token)
    {
        if (!supported || installation == null) throw new NotSupportedException(description);
        if (store)
        {
            var updates = (await Store().GetAppAndOptionalStorePackageUpdatesAsync().AsTask(token))
                .Where(update => update.Package.Id.FamilyName == family).ToArray();
            return updates.Length == 0 ? null : new("A Microsoft Store update is available") { Data = updates };
        }
        var release = await new GithubUpdates(http).CheckAsync(installation, token);
        return release == null ? null : new($"FluentDL {release.Version} is available") { Data = release };
    }

    public async Task DownloadAsync(AppUpdateOffer offer, IProgress<double> progress, CancellationToken token)
    {
        if (!supported || installation == null) throw new NotSupportedException(description);
        if (store && offer.Data is StorePackageUpdate[] updates)
        {
            var operation = Store().RequestDownloadStorePackageUpdatesAsync(updates);
            operation.Progress = (_, status) => progress.Report(status.TotalDownloadProgress * 100);
            RequireCompleted(await operation.AsTask(token));
            return;
        }
        if (!store && offer.Data is GithubUpdate release)
        {
            var directory = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Updates", Guid.NewGuid().ToString("N"));
            prepared[offer] = await new GithubUpdates(http).DownloadAsync(release, installation, directory, progress, token);
            return;
        }
        throw new InvalidOperationException("Check for updates again before downloading.");
    }

    public async Task InstallAsync(AppUpdateOffer offer, CancellationToken token)
    {
        if (!supported || installation == null) throw new NotSupportedException(description);
        if (store && offer.Data is StorePackageUpdate[] updates)
        {
            RequireCompleted(await Store().RequestDownloadAndInstallStorePackageUpdatesAsync(updates).AsTask(token));
            return;
        }
        if (store || !prepared.TryGetValue(offer, out var package)) throw new InvalidOperationException("Download the update first.");
        GithubUpdates.ValidatePackage(package.PackagePath, installation, package.Version);
        var directory = Path.Combine(Path.GetDirectoryName(package.PackagePath)!, "install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var planPath = Path.Combine(directory, "install-plan.json");
        var ready = Path.Combine(directory, "ready.json");
        var proceed = Path.Combine(directory, "proceed");
        var nonce = Guid.NewGuid().ToString("N");
        using var current = Process.GetCurrentProcess();
        var plan = new
        {
            installation.Name, installation.Publisher, installation.Architecture,
            Version = package.Version.ToString(), PackagePath = package.PackagePath, package.Sha256, package.Size,
            package.ArchivePath, package.Tag,
            ProcessId = current.Id, Started = current.StartTime.ToUniversalTime().Ticks,
            UserSid = WindowsIdentity.GetCurrent().User!.Value, Aumid = family + "!App", Nonce = nonce,
        };
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), token);
        var helper = Path.Combine(packageRoot, "Assets", "Updater", "InstallUpdate.ps1");
        if (!File.Exists(helper)) throw new FileNotFoundException("The update installer helper is missing.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = true, Verb = "runas",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{helper}\" -PlanPath \"{planPath}\"",
        };
        Process installerProcess;
        try { installerProcess = Process.Start(start) ?? throw new IOException("Windows did not start the installer."); }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        { throw new OperationCanceledException("Administrator permission was canceled.", error); }
        using var installer = installerProcess;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (File.Exists(ready))
                {
                    using var reply = JsonDocument.Parse(await File.ReadAllTextAsync(ready, deadline.Token));
                    if (reply.RootElement.GetProperty("Nonce").GetString() != nonce) throw new InvalidDataException("Invalid installer response.");
                    if (!reply.RootElement.GetProperty("Ready").GetBoolean()) throw new IOException("The installer could not validate this update. See the installer window.");
                    break;
                }
                if (installer.HasExited) throw new IOException("The installer stopped before it was ready. FluentDL has not closed.");
                await Task.Delay(200, deadline.Token);
            }
            await File.WriteAllTextAsync(proceed, nonce, deadline.Token);
            App.MainWindow.Close();
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cancel"), nonce, CancellationToken.None);
            throw;
        }
    }

    private static void RequireCompleted(StorePackageUpdateResult result)
    {
        if (result.OverallState == StorePackageUpdateState.Canceled) throw new OperationCanceledException("The Store update was canceled.");
        if (result.OverallState != StorePackageUpdateState.Completed) throw new IOException($"Microsoft Store update did not complete: {result.OverallState}.");
    }
}
