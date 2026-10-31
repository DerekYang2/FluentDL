using CommunityToolkit.Mvvm.ComponentModel;
using FluentDL.Contracts.Services;
using FluentDL.Core.Updates;

namespace FluentDL.ViewModels;

public sealed class AppUpdateViewModel(IAppUpdateService service) : ObservableObject
{
    private AppUpdateOffer? offer;
    private bool busy;
    private bool downloaded;
    private string message = "Check for updates when you are ready.";
    private double percent;
    private CancellationTokenSource? cancellation;
    public string InstallationDescription => service.InstallationDescription;
    public Uri HelpUri => service.HelpUri;
    public string HelpLabel => service.HelpLabel;
    public bool IsBusy => busy;
    public bool IsIndeterminate => busy && percent <= 0;
    public bool CanCheck => !busy;
    public bool CanAct => !busy && offer != null;
    public bool CanCancel => cancellation != null;
    public bool IsDownloaded => downloaded;
    public string ActionText => downloaded ? "Install update" : "Download update";
    public string Message => message;
    public double Percent => percent;

    public async Task CheckAsync()
    {
        if (busy) return;
        offer = null;
        downloaded = false;
        await RunAsync(async token =>
        {
            offer = await service.CheckAsync(token);
            message = offer?.DisplayVersion ?? "You are up to date for this update channel.";
        }, "Checking for updates...");
    }

    public async Task DownloadAsync()
    {
        if (!CanAct || downloaded || offer == null) return;
        var current = offer;
        await RunAsync(async token =>
        {
            var progress = new Progress<double>(value => { if (busy && ReferenceEquals(offer, current)) { percent = Math.Clamp(value, 0, 100); OnPropertyChanged(nameof(Percent)); OnPropertyChanged(nameof(IsIndeterminate)); } });
            await service.DownloadAsync(current, progress, token);
            token.ThrowIfCancellationRequested();
            downloaded = true;
            percent = 100;
            message = "Downloaded and ready. Installation may close FluentDL. Active work must finish first.";
        }, "Downloading update...");
    }

    public async Task InstallAsync(OperationGate gate, Func<CancellationToken, Task> prepare)
    {
        if (!CanAct || !downloaded || offer == null) return;
        using var reservation = gate.TryEnterInstallation();
        if (reservation == null) { message = "Finish active downloads, conversions, playlist uploads or file writes before installing."; Changed(); return; }
        busy = true;
        message = "Saving the queue and preparing installation...";
        Changed();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await prepare(deadline.Token);
            message = "Waiting for Windows to install the update...";
            Changed();
            await service.InstallAsync(offer, CancellationToken.None);
            message = "Update handed to Windows. Reopen FluentDL after installation finishes.";
            offer = null;
            downloaded = false;
        }
        catch (OperationCanceledException) { message = "Installation canceled. FluentDL has not requested a forced shutdown."; }
        catch (Exception error) { message = "Could not install the update: " + SafeMessage(error); }
        finally { busy = false; Changed(); }
    }

    public void Cancel() => cancellation?.Cancel();

    private async Task RunAsync(Func<CancellationToken, Task> action, string text)
    {
        using var current = cancellation = new CancellationTokenSource();
        busy = true;
        percent = 0;
        message = text;
        Changed();
        try { await action(current.Token); }
        catch (OperationCanceledException) { message = "Update operation canceled."; }
        catch (Exception error) { message = "Could not complete the update operation: " + SafeMessage(error); }
        finally { cancellation = null; busy = false; Changed(); }
    }

    private static string SafeMessage(Exception error)
    {
        Serilog.Log.Warning("App update failed: {ExceptionType}", error.GetType().Name);
        return error is NotSupportedException or InvalidDataException or IOException or InvalidOperationException
            ? error.Message : "Try again, or use the update source link below.";
    }

    private void Changed()
    {
        foreach (var property in new[] { nameof(IsBusy), nameof(IsIndeterminate), nameof(CanCheck), nameof(CanAct), nameof(CanCancel), nameof(IsDownloaded), nameof(ActionText), nameof(Message), nameof(Percent) })
            OnPropertyChanged(property);
    }
}
