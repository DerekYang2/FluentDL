using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Matching;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Pins the output of the pure string helpers that matching, naming and URLs depend on.
/// No network. Phase 1 moves these helpers into FluentDL.Core, and these baselines must not change.
/// </summary>
[TestClass]
public class HelperTests
{
    private static readonly string[] Titles =
    [
        "Get Lucky (feat. Pharrell Williams and Nile Rodgers)",
        "Get Lucky (Radio Edit - feat. Pharrell Williams and Nile Rodgers)",
        "Get Lucky (ft. Pharrell)",
        "Stay (with Justin Bieber)",
        "Song (Featuring Someone)",
        "Levels - Radio Edit",
        "Levels (Skrillex Remix)",
        "Come Together - Remastered 2009",
        "Yesterday [Remastered 2015]",
        "Halo",
        "Beyoncé",
        "Hoppípolla",
        "Sigur Rós",
        "夜に駆ける",
        "Cello Suite No. 1 in G Major, BWV 1007: I. Prélude",
        "Mr. Brightside",
        "Don't Stop Me Now",
        "\"Quoted\" Title",
        "Under — Pressure",
        "",
        "   ",
        "(",
        "Unclosed (feat. Someone",
        "Unclosed (ft. Someone",
        "Unclosed (with Someone",
    ];

    private static readonly (string, string)[] Pairs =
    [
        ("Get Lucky", "Get Lucky (feat. Pharrell Williams)"),
        ("Levels", "Levels (Skrillex Remix)"),
        ("Levels - Radio Edit", "Levels (Radio Edit)"),
        ("Black Eyed Peas", "The Black Eyed Peas"),
        ("Beyoncé", "Beyonce"),
        ("Halo", "Halo (Live)"),
        ("Random Access Memories", "Random Access Memories (10th Anniversary Edition)"),
        ("夜に駆ける", "Yoru ni Kakeru"),
        ("", "Something"),
        ("Daft Punk", "daft punk"),
    ];

    [TestMethod]
    public void TitleNormalizers()
    {
        var rows = Titles.Select(t => new
        {
            Input = t,
            ApiHelper_PruneTitle = Capture(() => ApiHelper.PruneTitle(t)),
            ApiHelper_PruneFeaturing = Capture(() => ApiHelper.PruneFeaturing(t)),
            ApiHelper_PrunePunctuation = Capture(() => TextNormalization.PrunePunctuation(t)),
            ApiHelper_EnforceAscii = Capture(() => TextNormalization.RemoveAccentsAndSymbols(t)),
            ApiHelper_RemoveDiacritics = Capture(() => ApiHelper.RemoveDiacritics(t)),
            DeezerApi_PruneTitle = Capture(() => TestSources.Deezer.PruneTitle(t)),
            DeezerApi_PruneTitleSearch = Capture(() => TestSources.Deezer.PruneTitleSearch(t)),
        });

        Snapshot.Match("Helpers.TitleNormalizers", rows);
    }

    [TestMethod]
    public void FuzzyComparisons()
    {
        var rows = Pairs.SelectMany(p => new[] { p, (p.Item2, p.Item1) }).Select(p => new
        {
            A = p.Item1,
            B = p.Item2,
            CloseMatch = Capture(() => ApiHelper.CloseMatch(p.Item1, p.Item2)),
            IsSubstring = Capture(() => ApiHelper.IsSubstring(p.Item1, p.Item2)),
            Levenshtein = Capture(() => ApiHelper.CalcLevenshteinDistance(p.Item1, p.Item2)),
        });

        Snapshot.Match("Helpers.FuzzyComparisons", rows);
    }

    [TestMethod]
    public void FileNames()
    {
        string[] names = ["AC/DC: Back in Black?", "CON", "con.mp3", "trailing dots...", "  spaced  ", "a<b>c|d*e", "", "Track.flac", "Track.FLAC", "Track.txt", "No extension", "Folder/Sub\\Name"];
        var rows = names.Select(n => new
        {
            Input = n,
            GetSafeFilename = Capture(() => ApiHelper.GetSafeFilename(n)),
            GetSafeDirectoryPath = Capture(() => ApiHelper.GetSafeDirectoryPath(n)),
            RemoveExtension = Capture(() => ApiHelper.RemoveExtension(n)),
        });

        Snapshot.Match("Helpers.FileNames", rows);
    }

    [TestMethod]
    public void Wildcards()
    {
        var song = new SongSearchObject
        {
            Source = "deezer", Id = "67238735", Title = "Get Lucky (feat. Pharrell Williams and Nile Rodgers)",
            Artists = "Daft Punk, Pharrell Williams, Nile Rodgers", AlbumName = "Random Access Memories",
            Isrc = "USQX91300108", TrackPosition = "8", ReleaseDate = "2013-05-17",
        };
        string[] wildcards = ["{artist} - {title}", "{position}. {title}", "{artists_sorted}/{album} ({year})", "{isrc}_{id}", "{date}", "{album}/{artists}", "plain"];
        var noDate = new SongSearchObject { Source = "youtube", Id = "x", Title = "A/B\\C", Artists = "", ReleaseDate = "" };

        var rows = wildcards.Select(w => new
        {
            Wildcard = w,
            Full = Capture(() => ApiHelper.EvaluateWildcard(song, w)),
            Sparse = Capture(() => ApiHelper.EvaluateWildcard(noDate, w)),
        });

        Snapshot.Match("Helpers.Wildcards", rows);
    }

    [TestMethod]
    public void Urls()
    {
        string[] sources = ["deezer", "qobuz", "spotify", "youtube", "local", "unknown"];
        var rows = sources.Select(s => new
        {
            Source = s,
            Track = Capture(() => ApiHelper.GetUrl(new SongSearchObject { Source = s, Id = "ID123" })),
            Album = Capture(() => ApiHelper.GetUrl(new AlbumSearchObject { Source = s, Id = "ID123" })),
        });

        Snapshot.Match("Helpers.Urls", rows);
    }

    [TestMethod]
    public void SourceSpecificHelpers()
    {
        string[] performers =
        [
            "Daft Punk, MainArtist - Pharrell Williams, FeaturedArtist - Nile Rodgers, FeaturedArtist, Composer - Thomas Bangalter, Producer",
            "Yo-Yo Ma, Performer - Johann Sebastian Bach, Composer",
            "Someone, Composer - Other, Lyricist",
            "",
        ];
        long[] counts = [0, 999, 1000, 15_400, 999_999, 1_000_000, 2_345_678_901];

        Snapshot.Match("Helpers.SourceSpecific", new
        {
            QobuzContributors = performers.Select(p => new { Input = p, Artists = Capture(() => TestSources.Qobuz.GetAllContributorsList(p)) }),
            QobuzGenres = Capture(() => TestSources.Qobuz.PruneGenreList(["Pop", "Musiques du monde", "Musiques du monde\u2192Europe", "Musiques du monde\u2192Europe\u2192Chanson française"])),
            YoutubeLargeValues = counts.Select(c => new { Input = c, Output = Capture(() => TestSources.Youtube.FormatLargeValue(c)) }),
        });
    }

    private static object? Capture<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            return $"throws {e.GetType().Name}";
        }
    }
}
