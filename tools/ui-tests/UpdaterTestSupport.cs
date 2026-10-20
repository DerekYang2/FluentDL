using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Windows.Storage;

namespace FluentDL;

// Included only in explicit preview output. No native installer or network updater is reachable through this backend.
internal static class UpdaterTestSupport
{
    internal static void Register(IServiceCollection services) => services.AddSingleton<IAppUpdateService>(_ => new PreviewUpdates());

    private sealed class PreviewUpdates : IAppUpdateService
    {
        private IDisposable? work;
        private int installs;
        private string Scenario => ApplicationData.Current.LocalSettings.Values.TryGetValue("UpdaterPreviewScenario", out var value) ? value.ToString()! : "current";
        public string InstallationDescription => "Synthetic update source. No installer will run.";
        public Uri HelpUri => new("https://github.com/DerekYang2/FluentDL/releases");
        public string HelpLabel => "Open releases";
        public async Task<AppUpdateOffer?> CheckAsync(CancellationToken token)
        {
            work?.Dispose();
            work = null;
            await Task.Delay(100, token);
            if (Scenario == "check-error") throw new IOException("Synthetic update-check failure.");
            if (Scenario == "busy") work = ApplicationActivity.Gate.EnterWork();
            return Scenario == "current" ? null : new("FluentDL 9.0.0.0 is available") { Data = Scenario };
        }
        public async Task DownloadAsync(AppUpdateOffer offer, IProgress<double> progress, CancellationToken token)
        {
            if (Scenario == "download-error") throw new IOException("Synthetic download failure.");
            if (Scenario == "slow") { await Task.Delay(Timeout.InfiniteTimeSpan, token); return; }
            progress.Report(25);
            await Task.Delay(150, token);
            progress.Report(100);
        }
        public Task InstallAsync(AppUpdateOffer offer, CancellationToken token)
        {
            installs++;
            var file = Path.Combine(ApplicationData.Current.LocalFolder.Path, "updater-preview.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                installs, nativeInstallerCalled = false, reserved = ApplicationActivity.Gate.IsInstalling,
                backups = Directory.GetFiles(Path.Combine(ApplicationData.Current.LocalFolder.Path, "Updates"), "queue-before-update-*.zip").Length,
            }));
            if (Scenario == "install-error") throw new IOException("Synthetic installer failure.");
            return Task.CompletedTask;
        }
    }
}
