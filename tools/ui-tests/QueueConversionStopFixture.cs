using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;

namespace FluentDL.Views;

public sealed partial class QueuePage
{
    internal sealed class QueueStopProbe : IConversionTarget
    {
        internal int Calls;
        internal int Cancelled;
        public SourceDescriptor Descriptor => SourceCatalog.Find(SourceIds.Spotify)!;

        public async Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            if (song.Id == "stop-not-found") return new(null, MatchKind.NotFound);
            if (song.Id == "stop-error") throw new InvalidOperationException("Synthetic provider failure.");
            if (song.Id != "stop-exact")
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref Cancelled);
                    // Exercise a provider that returns a late match instead of propagating cancellation.
                    if (song.Id != "stop-cancel-3") throw;
                }
            }
            return new(new SongSearchObject
            {
                Source = SourceIds.Spotify, Id = "converted-" + song.Id, Title = song.Title, Artists = song.Artists,
                Duration = song.Duration, Isrc = song.Isrc,
            }, MatchKind.Exact);
        }
    }

    internal static QueuePage CreateStopFixture(QueueStopProbe probe) => new(new ConversionService(new SourceRegistry([probe])));

    internal Task RunStopFixtureAsync()
    {
        OutputComboBox.SelectedItem = SourceCatalog.Find(SourceIds.Spotify)!.DisplayName;
        return ConversionTask([SourceIds.Qobuz], SourceIds.Spotify);
    }

    internal string InspectStopFixture(QueueStopProbe probe)
    {
        var markedCancelled = QueueViewModel.Source.Count(row => row.Id.StartsWith("stop-cancel-") && row.ConvertBadgeVisibility == Visibility.Visible);
        var darkErrors = QueueViewModel.Source.Count(row => row.ConvertBadgeVisibility == Visibility.Visible
            && row.ConvertBadgeColor.Color == Windows.UI.Color.FromArgb(255, 255, 0, 0));
        var pinkErrors = QueueViewModel.Source.Count(row => row.ConvertBadgeVisibility == Visibility.Visible
            && row.ConvertBadgeColor.Color == Windows.UI.Color.FromArgb(255, 255, 153, 164));
        return $"stopCalls={probe.Calls}; stopCancelled={probe.Cancelled}; stopSuccess={successSource.Count}; stopErrors={errorSource.Count}; stopMarkedCancelled={markedCancelled}; stopDarkErrors={darkErrors}; stopPinkErrors={pinkErrors}; stopRunning={QueueViewModel.Source.Count(row => row.IsRunning)}";
    }
}
