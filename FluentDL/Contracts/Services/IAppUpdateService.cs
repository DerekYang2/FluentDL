namespace FluentDL.Contracts.Services;

public sealed record AppUpdateOffer(string DisplayVersion)
{
    internal object? Data { get; init; }
}

public interface IAppUpdateService
{
    string InstallationDescription { get; }
    Uri HelpUri { get; }
    string HelpLabel { get; }
    Task<AppUpdateOffer?> CheckAsync(CancellationToken token);
    Task DownloadAsync(AppUpdateOffer offer, IProgress<double> progress, CancellationToken token);
    Task InstallAsync(AppUpdateOffer offer, CancellationToken token);
}
