using FluentDL.Contracts.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Services;

/// <summary>
/// Converts a track from one source to another (the queue's "convert" feature). It fills in the song's ISRC from
/// its own source when the song has none, so the target never has to call another source.
/// </summary>
public sealed class ConversionService(SourceRegistry sources)
{
    public async Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string targetId, CancellationToken token)
    {
        if (!sources.TryGet<IConversionTarget>(targetId, out var target))
        {
            throw new Exception("Unspecified output source");
        }
        return await target.FindEquivalentAsync(song, await IsrcOf(song), token);
    }

    // Only Spotify web player results lack an ISRC; Spotify is the only source with an ISRC lookup.
    private async Task<string?> IsrcOf(SongSearchObject song) =>
        string.IsNullOrWhiteSpace(song.Isrc) && sources.TryGet<IIsrcSource>(song.Source, out var origin)
            ? await origin.GetIsrcAsync(song.Id)
            : song.Isrc;
}
