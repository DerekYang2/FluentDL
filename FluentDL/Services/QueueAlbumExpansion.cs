using FluentDL.Contracts.Sources;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Services.Sources;

namespace FluentDL.Services;

internal sealed record QueueAlbumExpansionPlan(IReadOnlyList<QueueObject> Rows, int Skipped);

internal sealed class QueueAlbumExpansion(SourceRegistry sources)
{
    internal async Task<IReadOnlyList<SongSearchObject>> LoadAsync(QueueObject row)
    {
        if (!row.IsAlbum) throw new InvalidOperationException("This queue item is not an album.");
        if (!sources.TryGet<ICatalogSource>(row.Source, out var catalog))
            throw new InvalidOperationException("This source cannot list album tracks.");
        var album = (AlbumSearchObject)QueueObject.Unwrap(row);
        var tracks = await catalog.GetAlbumTracksAsync(album);
        Validate(row, tracks);
        return tracks;
    }

    private static void Validate(QueueObject row, IReadOnlyList<SongSearchObject> tracks)
    {
        if (!row.IsAlbum) throw new InvalidOperationException("This queue item is not an album.");
        if (tracks.Count == 0 || tracks.Count < row.AlbumTracksCount.GetValueOrDefault())
            throw new InvalidOperationException($"The source returned {tracks.Count} of {row.AlbumTracksCount} album tracks. The album was left unchanged.");
        if (tracks.Any(track => track == null || string.IsNullOrWhiteSpace(track.Id)
            || track.Source != row.Source || QueueObject.IsAlbumItem(track)))
            throw new InvalidOperationException("The source returned an invalid album track list. The album was left unchanged.");
    }

    internal static QueueAlbumExpansionPlan Plan(QueueObject album, IReadOnlyList<SongSearchObject> tracks, IEnumerable<QueueObject> queue)
    {
        Validate(album, tracks);
        var existing = queue.Where(row => !row.IsAlbum).Select(row => (row.Source, row.Id)).ToHashSet();
        var rows = new List<QueueObject>();
        var skipped = 0;
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (!existing.Add((track.Source, track.Id))) { skipped++; continue; }
            rows.Add(new QueueObject(track)
            {
                QueueCounter = album.QueueCounter,
                QueueSubOrder = i,
                LocalBitmapImage = track.LocalBitmapImage ?? album.LocalBitmapImage,
            });
        }
        return new(rows, skipped);
    }
}
