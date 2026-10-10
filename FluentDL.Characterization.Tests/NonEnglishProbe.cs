using System.Text;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Helpers;
using FluentDL.Models;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Investigation, not a regression test: how each service names non-English recordings, whether a native-language
/// search finds them, and whether metadata matching (no ISRC, as for local files and YouTube) finds the same recording
/// on the other services. Writes a UTF-8 report to %TEMP%\opencode\non-english-report.md.
/// Run with: dotnet test ... --filter "TestCategory=NonEnglishProbe"
/// </summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("NonEnglishProbe")]
public class NonEnglishProbe
{
    private sealed record Example(string Key, string Isrc, string NativeArtist, string NativeTitle, string NativeAlbum);

    private static readonly Example[] Examples =
    [
        new("ja-yoasobi", "JPP301900716", "YOASOBI", "夜に駆ける", "夜に駆ける"),
        new("ja-yonezu", "JPU901800227", "米津玄師", "Lemon", "Lemon"),
        new("ja-lisa", "JPU901900905", "LiSA", "紅蓮華", "紅蓮華"),
        new("ja-aimer", "JPU902104729", "Aimer", "残響散歌", "残響散歌"),
        new("ko-bts", "KRA381700253", "방탄소년단", "봄날", "YOU NEVER WALK ALONE"),
        new("ko-iu", "KRA381700868", "아이유", "밤편지", "Palette"),
        new("zh-jay", "TWK970300503", "周杰倫", "晴天", "葉惠美"),
        new("zh-gem", "HKI111700001", "G.E.M. 鄧紫棋", "光年之外", "光年之外"),
        new("ru-kino", "RUB421401310", "Кино", "Группа крови", "Группа крови"),
        new("ar-fairuz", "QZ4UU1707654", "فيروز", "شادي", "Emany Satea"),
        new("fr-farmer", "FRY539100020", "Mylène Farmer", "Désenchantée", "L'Autre..."),
        new("es-rosalia", "ES5021800275", "ROSALÍA", "MALAMENTE", "MALAMENTE"),
    ];

    private static readonly string[] Targets = ["deezer", "qobuz", "spotify"];

    [TestMethod]
    [Timeout(30 * 60 * 1000)]
    public async Task Report()
    {
        await LiveSources.EnsureDeezerAsync();
        await LiveSources.EnsureQobuzAsync();
        await LiveSources.EnsureSpotifyOfficialAsync();
        var token = CancellationToken.None;
        var report = new StringBuilder("# Non-English probe\n\n");

        foreach (var example in Examples)
        {
            report.AppendLine($"## {example.Key}: {example.NativeArtist} - {example.NativeTitle} ({example.Isrc})\n");

            // 1. How each service names the recording.
            var named = new Dictionary<string, SongSearchObject?>
            {
                ["deezer"] = await Safe(() => TestSources.Deezer.GetTrackFromISRC(example.Isrc)),
                ["qobuz"] = await Safe(async () => await TestSources.Qobuz.SearchISRC(example.Isrc) is { } t ? TestSources.Qobuz.ConvertSongSearchObject(t) : null),
                ["spotify"] = await Safe(() => TestSources.Spotify.GetTrackFromISRC(example.Isrc)),
            };
            report.AppendLine("| Service | Title | Artists | Album | File name |");
            report.AppendLine("|---|---|---|---|---|");
            foreach (var (service, song) in named)
            {
                report.AppendLine(song == null
                    ? $"| {service} | not found by ISRC | | | |"
                    : $"| {service} | {song.Title} | {song.Artists} | {song.AlbumName} | {ApiHelper.GetSafeFilename(ApiHelper.EvaluateWildcard(song, "{position}. {artist} - {title} [{isrc}]"))} |");
            }

            // 2. Native-language search: the top 5 on each service, and where the recording is among them.
            report.AppendLine("\n| Search (native) | Rank of the recording | Top results |");
            report.AppendLine("|---|---|---|");
            var query = $"{example.NativeArtist} {example.NativeTitle}";
            foreach (var service in Targets)
            {
                var results = new List<SongSearchObject>();
                TestSources.Registry.TryGet<Contracts.Sources.ISearchSource>(service, out var source);
                await Safe(async () => { await source!.SearchAsync(results, query, new(0, 5), albumMode: false, token); return (SongSearchObject?)null; });
                var rank = results.FindIndex(r => named[service] != null && r.Id == named[service]!.Id);
                report.AppendLine($"| {service} | {(rank < 0 ? "not in top 5" : (rank + 1).ToString())} | {string.Join(" / ", results.Select(r => $"{r.Title} ({r.Artists})"))} |");
            }

            // 3. Metadata conversion with no ISRC, from each service's naming and from the native names.
            report.AppendLine("\n| Convert from (no ISRC) | To | Result |");
            report.AppendLine("|---|---|---|");
            var origins = named.Where(n => n.Value != null).Select(n => (Name: $"{n.Key} naming", Song: n.Value!)).ToList();
            // A local file tagged in the native language; it has its own length, taken here from a service.
            var length = named.Values.FirstOrDefault(n => n != null)?.Duration ?? "0";
            origins.Add(("native names", new SongSearchObject { Title = example.NativeTitle, Artists = example.NativeArtist, AlbumName = example.NativeAlbum, Duration = length }));
            foreach (var (name, origin) in origins)
            {
                foreach (var target in Targets)
                {
                    if (name == $"{target} naming") continue;
                    var song = new SongSearchObject
                    {
                        Source = "local", Id = "probe", Title = origin.Title, Artists = origin.Artists, AlbumName = origin.AlbumName,
                        Duration = origin.Duration, ReleaseDate = origin.ReleaseDate, Isrc = null,
                    };
                    var match = await Safe(() => TestSources.Conversions.FindEquivalentAsync(song, target, token));
                    var verdict = match switch
                    {
                        null => "threw",
                        { Track: null } => match.Kind.ToString(),
                        { Track: var t } when named[target] != null && t.Id == named[target]!.Id => $"{match.Kind}, right recording",
                        { Track: var t } when t.Isrc == example.Isrc => $"{match.Kind}, right recording (another release)",
                        { Track: var t } => $"{match.Kind}, other track: {t.Title} ({t.Artists}) {t.Isrc}",
                    };
                    report.AppendLine($"| {name} | {target} | {verdict} |");
                }
            }
            report.AppendLine();
            File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
        }
    }

    private static string ReportPath => Path.Combine(Path.GetTempPath(), "opencode", "non-english-report.md");

    private static async Task<T?> Safe<T>(Func<Task<T?>> call) where T : class
    {
        try { return await call(); } catch (Exception) { return null; }
    }
}
