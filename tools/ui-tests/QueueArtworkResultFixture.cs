using FluentDL.Models;

namespace FluentDL.Views;

public sealed partial class QueuePage
{
    internal async Task ShowArtworkResultFixtureAsync(SongSearchObject result)
    {
        successSource.Clear();
        warningSource.Clear();
        errorSource.Clear();
        successSource.TryAdd(result, (0, 0));
        await ShowConversionDialog();
    }
}
