namespace FluentDL.Characterization.Tests;

/// <summary>
/// Known songs used across the live tests. See the "Golden fixtures" note in the refactor plan.
/// Qobuz ids come from Qobuz's own ISRC search (checked to return the same ISRC). Spotify and YouTube ids
/// were taken from the conversion baseline recorded from master and checked by hand.
/// </summary>
internal static class Fixtures
{
    public sealed record Track(string Key, string? Isrc, string? DeezerId, string? QobuzId = null, string? SpotifyId = null, string? YoutubeId = null);

    public static readonly Track[] Tracks =
    [
        new("get-lucky-album", "USQX91300108", "67238735", QobuzId: "9140031", SpotifyId: "69kOkLUCkxIZYexIgSG8rq", YoutubeId: "4D7u5KF7SP8"),
        new("get-lucky-radio-edit", "USQX91300809", "66609426", QobuzId: "8767428", SpotifyId: "2Foc5Q5nqNiosCNqttzHof", YoutubeId: "Rgrt_8mXrK8"),
        new("beyonce-halo", "USSM10804556", "2553265", QobuzId: "460985", SpotifyId: "4JehYebiI9JE8sR8MisGVb", YoutubeId: "-3Wt6wwlmC8"),
        new("yoasobi-yoru-ni-kakeru", "JPP301900716", "824731152", QobuzId: "82096207", SpotifyId: "3dPtXHP0oXQ4HCWHsOA9js", YoutubeId: "by4SYYWlhEs"),
        new("avicii-levels-original", "SEUM71100961", "14383881", QobuzId: "8632879", SpotifyId: "6Xe9wT5xeZETPwtaP2ynUz", YoutubeId: "wDuoOapZ9Z0"),
        new("avicii-levels-skrillex-remix", "SEUM71101146", "15421140", QobuzId: "8636159", SpotifyId: "5f6igUw1asUL4AGXSvgTW8", YoutubeId: "u6cdQFpP88w"),
        new("yo-yo-ma-bwv1007-prelude", "USSM18200515", "584661", QobuzId: "5590843", SpotifyId: "17i5jLpzndlQhbS4SrTd0B", YoutubeId: "noW9fM9vgJs"),
        new("youtube-only-me-at-the-zoo", null, null, YoutubeId: "jNQXAC9IVRw"),
    ];

    // Daft Punk, Random Access Memories
    public const string DeezerAlbumId = "6575789";
    public const string QobuzAlbumId = "0886443927087";
    public const string SpotifyAlbumId = "4m2880jivSbbyEGAKfITCa";
    public const string AlbumUpc = "886443927087";

    public const string QobuzLabelId = "4592";
    public const string QobuzArtistId = "36819"; // Daft Punk
}
