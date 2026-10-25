using FluentDL.Models;

namespace FluentDL.Views;

// Included only by the compact-layout UI fixture. No matching, downloads or conversions run here.
public sealed partial class QueuePage
{
    internal async Task ShowCompactSummaryFixtureAsync(IReadOnlyList<SongSearchObject> songs)
    {
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        for (var index = 14; index >= 0; index--)
        {
            var bucket = index < 5 ? successSource : index < 10 ? warningSource : errorSource;
            var song = songs[index];
            bucket.TryAdd(index % 5 == 0 ? new AlbumSearchObject
            {
                Title = $"Fixture album result {index / 5 + 1}", Artists = song.Artists,
                TracksCount = 12, Duration = song.Duration, ReleaseDate = song.ReleaseDate,
                LocalBitmapImage = song.LocalBitmapImage,
            } : song, (index, 0));
        }
        await ShowConversionDialog();
    }
}
