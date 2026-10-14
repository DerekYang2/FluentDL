#nullable enable
namespace FluentDL.Core.Matching;

public enum AlbumMatchLevel
{
    None,
    Close,
    Exact,
}

/// <summary>
/// Whether two albums from different services are the same release, for album conversion when no UPC lookup finds it.
/// </summary>
public static class AlbumMatch
{
    // Different recordings. Edition words such as "Deluxe", "Edition" and "Remastered" do not count.
    private static readonly string[] RecordingKeywords = ["live", "remix", "instrumental", "acoustic", "karaoke"];

    /// <summary>
    /// Exact: the same title after <see cref="TitleNormalizer.Compact"/>, an artist in common, and the same track count.
    /// Close: the same main title (before any bracket or " - " suffix), an artist in common, and the same recording
    /// keywords, such as a deluxe or anniversary edition. A track count of 0 means unknown and never blocks Exact.
    /// </summary>
    public static AlbumMatchLevel Compare(string title, string artists, int tracks, string otherTitle, string otherArtists, int otherTracks)
    {
        if (!FuzzyMatch.AnyArtistMatches(artists, otherArtists)) return AlbumMatchLevel.None;
        var lower = title.ToLowerInvariant();
        var otherLower = otherTitle.ToLowerInvariant();
        if (RecordingKeywords.Any(k => lower.Contains(k) != otherLower.Contains(k))) return AlbumMatchLevel.None;
        var sameTitle = Key(title) is { Length: > 0 } key && key == Key(otherTitle);
        var sameCount = tracks <= 0 || otherTracks <= 0 || tracks == otherTracks;
        if (sameTitle && sameCount) return AlbumMatchLevel.Exact;
        var main = Key(TitleNormalizer.MainTitle(title));
        return sameTitle || main.Length > 0 && main == Key(TitleNormalizer.MainTitle(otherTitle))
            ? AlbumMatchLevel.Close : AlbumMatchLevel.None;
    }

    // Compact drops non-Latin scripts entirely, so 光年之外 would match anything empty. Compare those as written.
    private static string Key(string title) =>
        TitleNormalizer.Compact(title) is { Length: > 0 } compact ? compact : title.Trim().ToLowerInvariant();
}
