using FluentDL.Contracts.Sources;
using FluentDL.Core.Helpers;
using FluentDL.Core.Matching;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Services;

/// <summary>
/// Converts a track from one source to another (the queue's "convert" feature). Targets match by ISRC first, so this
/// finds one when it can: on the song, from the song's own source (Spotify web player results), or, for songs with
/// none (local files, YouTube videos), by finding the same recording on Spotify. The ISRC finds a recording whatever
/// each service calls it: Deezer lists 紅蓮華 as "Gurenge" and 봄날 as "Spring Day", which no name match connects.
/// </summary>
public sealed class ConversionService(SourceRegistry sources)
{
    // Spotify's top result counts as the same recording only at about the same length; covers and karaoke versions
    // usually differ.
    private static readonly TimeSpan BridgeLengthTolerance = TimeSpan.FromSeconds(2);

    public async Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string targetId, CancellationToken token)
    {
        if (!sources.TryGet<IConversionTarget>(targetId, out var target))
        {
            throw new Exception("Unspecified output source");
        }

        if (song is AlbumSearchObject album)
        {
            return await FindAlbumAsync(album, targetId, token);
        }

        var isrc = await IsrcOf(song);
        if (isrc != null)
        {
            return await target.FindEquivalentAsync(song, isrc, token);
        }

        var bridged = await FindOnBridge(song, token);
        if (bridged == null)
        {
            return await target.FindEquivalentAsync(song, null, token);
        }

        // Found by a search and its length, not by the song's own ISRC, so it is a likely match (yellow), not a
        // certain one. If the ISRC finds nothing on the target, the target still matches by name as before.
        var (bridgeTrack, bridgeIsrc, bridge) = bridged.Value;
        if (bridge.Descriptor.Id == targetId)
        {
            return new MatchResult(bridgeTrack, MatchKind.Close);
        }
        var match = await target.FindEquivalentAsync(song, bridgeIsrc, token);
        return match.Kind == MatchKind.Exact ? match with { Kind = MatchKind.Close } : match;
    }

    // An album's Isrc field holds its UPC. Services pad UPCs differently (12-digit UPC-A or 13-digit EAN with a leading 0).
    private static IEnumerable<string> UpcVariants(string upc)
    {
        upc = upc.Trim();
        yield return upc;
        if (upc.Length == 12) yield return "0" + upc;
        if (upc.Length == 13 && upc[0] == '0') yield return upc[1..];
    }

    /// <summary>
    /// The same album on the target: by UPC where the target can look one up (Exact), otherwise the best album search
    /// result by title, artist and track count (<see cref="AlbumMatch"/>).
    /// </summary>
    private async Task<MatchResult> FindAlbumAsync(AlbumSearchObject album, string targetId, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(album.Upc) && sources.TryGet<IAlbumUpcSource>(targetId, out var byUpc))
        {
            foreach (var upc in UpcVariants(album.Upc))
            {
                try
                {
                    if (await byUpc.GetAlbumByUpcAsync(upc, token) is { } found) return new MatchResult(found, MatchKind.Exact);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Serilog.Log.Warning(e, "Album UPC lookup on {Provider} failed", byUpc.Descriptor.DisplayName);
                    break;
                }
            }
        }

        if (!sources.TryGet<ISearchSource>(targetId, out var search)) return new MatchResult(null, MatchKind.NotFound);
        // Only album results count. Spotify's web player mode has no album search and returns tracks, so it finds none.
        var results = new List<SongSearchObject>();
        var query = $"{album.Artists.Split(", ")[0]} {TextNormalization.PruneFeaturing(album.Title)}";
        await search.SearchAsync(results, query, new PageRequest(0, 10), albumMode: true, token);
        var best = results.OfType<AlbumSearchObject>()
            .Select(candidate => (candidate, level: AlbumMatch.Compare(album.Title, album.Artists, album.TracksCount,
                candidate.Title, candidate.Artists, candidate.TracksCount)))
            .Where(c => c.level != AlbumMatchLevel.None)
            .OrderByDescending(c => c.level)
            .ThenBy(c => Math.Abs(c.candidate.TracksCount - album.TracksCount))
            .FirstOrDefault();
        return best.candidate == null
            ? new MatchResult(null, MatchKind.NotFound)
            : new MatchResult(best.candidate, best.level == AlbumMatchLevel.Exact ? MatchKind.Exact : MatchKind.Close);
    }

    // Only Spotify web player results lack an ISRC among the sources that have one; Spotify is the only source with an
    // ISRC lookup.
    private async Task<string?> IsrcOf(SongSearchObject song)
    {
        if (!string.IsNullOrWhiteSpace(song.Isrc))
        {
            return song.Isrc;
        }
        return sources.TryGet<IIsrcSource>(song.Source, out var origin) ? await origin.GetIsrcAsync(song.Id) : null;
    }

    // The top search result on a source that can search and give ISRCs (Spotify), with its ISRC, when it is as long as
    // the song. Null when the song has no length, or nothing fits, or the source fails.
    private async Task<(SongSearchObject Track, string Isrc, ISource Bridge)?> FindOnBridge(SongSearchObject song, CancellationToken token)
    {
        var bridge = sources.All<ISearchSource>().FirstOrDefault(s => s is IIsrcSource);
        if (bridge == null || bridge.Descriptor.Id == song.Source // its own ISRC lookup already failed above
            || !DurationParser.TryParse(song.Duration, out var length) || length == TimeSpan.Zero)
        {
            return null;
        }

        try
        {
            var results = new List<SongSearchObject>();
            var query = $"{song.Artists.Split(", ")[0]} {TextNormalization.PruneFeaturing(song.Title)}";
            await bridge.SearchAsync(results, query, new PageRequest(0, 1), albumMode: false, token);
            if (results is not [var top] || !DurationParser.TryParse(top.Duration, out var topLength)
                || (topLength - length).Duration() > BridgeLengthTolerance)
            {
                return null;
            }

            var isrc = await ((IIsrcSource)bridge).GetIsrcAsync(top.Id);
            if (string.IsNullOrWhiteSpace(isrc))
            {
                return null;
            }
            top.Isrc = isrc;
            return (top, isrc, bridge);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Serilog.Log.Warning(e, "Finding the recording on {Provider} for its ISRC failed", bridge.Descriptor.DisplayName);
            return null;
        }
    }
}
