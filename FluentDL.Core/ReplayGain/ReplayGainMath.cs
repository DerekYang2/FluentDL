#nullable enable
namespace FluentDL.Core.ReplayGain;

public static class ReplayGainMath
{
    // ReplayGain 2.0 normalizes tracks to -18 LUFS.
    public const double ReferenceLoudness = -18.0;

    // A silent track has no loudness to normalize, so it gets 0 dB like rsgain gives it.
    public static double Gain(double loudness, double offset) =>
        double.IsFinite(loudness) ? ReferenceLoudness - loudness + offset : 0.0;

    // Compared in dB with a tiny tolerance, so a gain that PreventClipping lowered to exactly full scale
    // doesn't count as clipping because of rounding.
    public static bool WouldClip(double gain, double peak) => peak > 0 && gain + 20 * Math.Log10(peak) > 1e-9;

    // Lowers a positive gain until the peak reaches full scale, but never below 0 dB. This matches rsgain's
    // default clipping protection: negative gains already lower the volume, so they are left alone. The result
    // is rounded down to 0.01 dB, the precision tags store, so the written value can't clip either.
    public static double PreventClipping(double gain, double peak)
    {
        if (gain <= 0 || !WouldClip(gain, peak)) return gain;
        // The small allowance keeps a limit such as 20 dB, which computes as 19.999999999999996, from rounding to 19.99.
        var limit = -20 * Math.Log10(peak);
        return Math.Max(0, Math.Floor(limit * 100 + 1e-7) / 100);
    }

    // How the app shows a gain, such as "+6.48 dB".
    public static string FormatGain(double gain) => $"{gain:+0.00;-0.00;0.00} dB";

    // Tracks group by album artist and album. A track without an album artist takes the album artist that the
    // same album's other tracks in its folder share, if there is exactly one. Otherwise the folder separates
    // same-named albums, and a track without an album name has its folder as the album.
    public static List<List<T>> GroupByAlbum<T>(IReadOnlyCollection<T> items, Func<T, ReplayGainTrack> trackOf, bool singleAlbum)
    {
        if (singleAlbum) return [items.ToList()];
        var folderArtists = items.Select(trackOf)
            .Where(track => !IsBlank(track.Album) && !IsBlank(track.AlbumArtist))
            .GroupBy(FolderAlbum, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(track => track.AlbumArtist!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        return items.GroupBy(item => AlbumKey(trackOf(item), folderArtists), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToList())
            .ToList();
    }

    private static string AlbumKey(ReplayGainTrack track, Dictionary<string, List<string>> folderArtists)
    {
        if (IsBlank(track.Album)) return $"folder\u0001{Folder(track)}";
        var artist = !IsBlank(track.AlbumArtist) ? track.AlbumArtist!.Trim()
            : folderArtists.TryGetValue(FolderAlbum(track), out var artists) && artists.Count == 1 ? artists[0]
            : $"folder\u0001{Folder(track)}";
        return $"album\u0001{artist}\u0001{track.Album!.Trim()}";
    }

    private static string FolderAlbum(ReplayGainTrack track) => $"{Folder(track)}\u0001{track.Album!.Trim()}";

    private static string Folder(ReplayGainTrack track) => Path.GetDirectoryName(Path.GetFullPath(track.Path)) ?? "";

    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);
}
