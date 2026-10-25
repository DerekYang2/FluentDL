using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentDL.Services.Sources;

internal static class DeezerGatewayTrackMapper
{
    // Convert only complete records. Missing or ambiguous metadata uses the full public track response instead.
    internal static bool TryConvert(JsonElement track, out JsonElement mapped)
    {
        mapped = default;
        if (track.ValueKind != JsonValueKind.Object) return false;
        string Text(string name) => track.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString() : string.Empty;
        var id = Text("SNG_ID");
        var albumId = Text("ALB_ID");
        var title = Text("SNG_TITLE");
        var album = Text("ALB_TITLE");
        var isrc = Text("ISRC");
        var duration = Text("DURATION");
        var rank = Text("RANK_SNG");
        var position = Text("TRACK_NUMBER");
        var release = Text("DIGITAL_RELEASE_DATE").Split(' ', 'T')[0];
        var explicitValue = Text("EXPLICIT_LYRICS");
        if (!long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit)
            || !long.TryParse(albumId, out var albumNumber) || albumNumber <= 0 || !albumId.All(char.IsAsciiDigit)
            || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(album)
            || !Regex.IsMatch(isrc, @"^[A-Za-z]{2}[A-Za-z0-9]{3}[0-9]{7}$")
            || !int.TryParse(duration, out var seconds) || seconds <= 0
            || !int.TryParse(position, out var trackNumber) || trackNumber <= 0
            || !int.TryParse(rank, out var rankNumber) || rankNumber < 0
            || !DateOnly.TryParseExact(release, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || explicitValue is not ("0" or "1")
            || !track.TryGetProperty("ARTISTS", out var artists) || artists.ValueKind != JsonValueKind.Array
            || !track.TryGetProperty("MEDIA", out var media) || media.ValueKind != JsonValueKind.Array) return false;

        var names = new List<string>();
        foreach (var artist in artists.EnumerateArray())
        {
            if (artist.ValueKind != JsonValueKind.Object || !artist.TryGetProperty("ART_NAME", out var name)
                || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())) return false;
            foreach (var part in name.GetString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!names.Contains(part)) names.Add(part);
        }
        if (names.Count == 0 || !names.Contains(Text("ART_NAME"))) return false;

        var preview = string.Empty;
        if (media.GetArrayLength() > 0)
        {
            var first = media[0];
            if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("HREF", out var href)
                || href.ValueKind != JsonValueKind.String || !Uri.TryCreate(href.GetString(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")) return false;
            preview = uri.AbsoluteUri;
        }
        var version = Text("VERSION").Trim();
        if (version.Length > 0 && !title.Contains(version, StringComparison.Ordinal)) title = $"{title} {version}";
        mapped = JsonSerializer.SerializeToElement(new
        {
            id, title, isrc, duration, rank, release_date = release, track_position = position,
            explicit_lyrics = explicitValue == "1", preview,
            contributors = names.Select(name => new { name }).ToArray(),
            album = new { title = album, cover = $"https://api.deezer.com/album/{albumId}/image" },
        });
        return true;
    }
}
