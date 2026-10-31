using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services;

internal sealed record DownloadedAlbumRow(SongSearchObject Song, int Order, StatusLevel Level);

internal static class QueueAlbumDownload
{
    internal static List<DownloadedAlbumRow> Resolve(IReadOnlyList<AlbumTrackDownloadOutcome> outcomes, Func<string, SongSearchObject?> parseFile)
    {
        var rows = new List<DownloadedAlbumRow>(outcomes.Count);
        foreach (var outcome in outcomes.OrderBy(outcome => outcome.Order))
        {
            SongSearchObject? local = null;
            if (outcome.Path != null)
            {
                try { local = parseFile(outcome.Path); }
                catch (Exception error) { Serilog.Log.Warning(error, "Downloaded album track could not be read; keeping its source row for retry"); }
            }
            rows.Add(local != null
                ? new(local, outcome.Order, outcome.Level)
                : new(outcome.Track, outcome.Order, StatusLevel.Error));
        }
        return rows;
    }
}
